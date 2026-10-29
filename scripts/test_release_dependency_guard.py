"""Cases for scripts/release_dependency_guard.py: which pins a release may ship (hermetic: a fake nuget.org).

Run: python -m unittest discover -s scripts -p "test_release_dependency_guard.py"
"""
import importlib.util
import json
import sys
import tempfile
import textwrap
import unittest
import urllib.error
from pathlib import Path
from unittest import mock

_SCRIPT = Path(__file__).resolve().parent / "release_dependency_guard.py"
_spec = importlib.util.spec_from_file_location("release_dependency_guard", _SCRIPT)
guard = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = guard  # dataclasses resolve annotations through sys.modules
_spec.loader.exec_module(guard)

COMMON = "McpPlugin.Common/McpPlugin.Common.csproj"
PLUGIN = "McpPlugin/McpPlugin.csproj"
SERVER = "McpPlugin.Server/McpPlugin.Server.csproj"
PUBLISHED = {
    "com.ivanmurzak.reflectornet": ["5.4.0", "5.5.0"],
    "microsoft.aspnetcore.signalr.client": ["8.0.15", "10.0.3"],
    "modelcontextprotocol": ["1.4.0"],
    "r3": ["1.3.0"],
}

# The shape of main while ReflectorNet is integrated unreleased (5.5.1-dev.<sha> from prerelease-packages/).
FEED_PROPS = """\
<Project>
  <!-- TEMPORARY pre-release feed: prerelease-packages/ -->
  <PropertyGroup>
    <RestoreAdditionalProjectSources>$(RestoreAdditionalProjectSources);$(MSBuildThisFileDirectory)prerelease-packages</RestoreAdditionalProjectSources>
  </PropertyGroup>
</Project>
"""


def fake_registry(published=PUBLISHED, fail=False):
    def fetch(url):
        if fail:
            raise guard.RegistryError("offline")
        package = url.split("/")[-2]
        versions = published.get(package)
        return None if versions is None else json.dumps({"versions": versions}).encode()
    return guard.NuGetRegistry(fetch)


def csproj(reflectornet="5.5.0", signalr="8.0.15", extra="", packable=True,
           reference="./../McpPlugin.Common/McpPlugin.Common.csproj"):
    project_reference = f'<ProjectReference Include="{reference}" />' if reference else ""
    return textwrap.dedent(f"""\
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <IsPackable>{str(packable).lower()}</IsPackable>
            <Version>8.13.1</Version>
          </PropertyGroup>
          <ItemGroup>
            {project_reference}
            <PackageReference Include="com.IvanMurzak.ReflectorNet" Version="{reflectornet}" />
            <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="{signalr}" />
            <PackageReference Include="R3" Version="1.3.0" />{extra}
          </ItemGroup>
        </Project>
        """)


class Fixture:
    """A minimal MCP-Plugin-dotnet tree: the three packed csprojs plus unshipped test/demo projects."""

    def __init__(self, root: Path, reflectornet="5.5.0", feed=False):
        self.root = root
        self.write(COMMON, csproj(reflectornet, reference=None))
        self.write(PLUGIN, csproj(reflectornet))
        self.write(SERVER, csproj(reflectornet, signalr="10.0.3",
                                  extra='\n    <PackageReference Include="ModelContextProtocol" Version="1.4.0" />'))
        # Test and demo projects ship nothing: their pins and local feeds must not trip the guard.
        self.write("McpPlugin.Tests/McpPlugin.Tests.csproj",
                   '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>'
                   '<ItemGroup><PackageReference Include="xunit" Version="3.0.0-pre.1" /></ItemGroup></Project>')
        self.write("McpPlugin.Tests/nuget.config",
                   '<configuration><packageSources><add key="local" value="../local-nuget" /></packageSources></configuration>')
        self.write("DemoWebApp/DemoWebApp.csproj",
                   '<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup>'
                   '<PackageReference Include="Unpublished.Thing" Version="0.0.1" /></ItemGroup></Project>')
        if feed:
            self.write("Directory.Build.props", FEED_PROPS)
            self.write(f"prerelease-packages/com.IvanMurzak.ReflectorNet.{reflectornet}.nupkg", "")

    def write(self, relative, text):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def problems(self, registry=None):
        _, problems = guard.run(self.root, registry or fake_registry())
        return [(guard.rel(p.file, self.root), p.message) for p in problems]

    def test_published_pins_pass(self):
        Fixture(self.root)
        pins, problems = guard.run(self.root, fake_registry())
        self.assertEqual([], problems)
        self.assertEqual({COMMON, PLUGIN, SERVER}, {guard.rel(p.file, self.root) for p in pins})
        self.assertEqual({("com.IvanMurzak.ReflectorNet", "5.5.0"), ("Microsoft.AspNetCore.SignalR.Client", "8.0.15"),
                          ("Microsoft.AspNetCore.SignalR.Client", "10.0.3"), ("R3", "1.3.0"),
                          ("ModelContextProtocol", "1.4.0")},
                         {(p.package, p.version) for p in pins})

    def test_prerelease_reflectornet_and_local_feed_fail(self):
        Fixture(self.root, reflectornet="5.5.1-dev.ec3702e", feed=True)
        problems = self.problems()
        for project in (COMMON, PLUGIN, SERVER):
            self.assertIn((project, "com.IvanMurzak.ReflectorNet 5.5.1-dev.ec3702e is a pre-release version"), problems)
        self.assertTrue(any(f == "Directory.Build.props" and "RestoreAdditionalProjectSources adds the local NuGet "
                            "feed '$(MSBuildThisFileDirectory)prerelease-packages'" in m for f, m in problems))
        self.assertFalse(any(f.startswith(("McpPlugin.Tests/", "DemoWebApp/")) for f, _ in problems))
        self.assertEqual(4, len(problems))  # the shared Directory.Build.props is reported once

    def test_feed_problem_points_at_the_property_line(self):
        Fixture(self.root, feed=True)
        _, problems = guard.run(self.root, fake_registry())
        self.assertEqual([("Directory.Build.props", 4)], [(guard.rel(p.file, self.root), p.line) for p in problems])

    def test_unpublished_stable_version_and_unknown_package_fail(self):
        Fixture(self.root, reflectornet="5.99.0")
        self.assertIn((PLUGIN, "com.IvanMurzak.ReflectorNet 5.99.0 is not published on nuget.org"), self.problems())
        unknown = self.problems(fake_registry({**PUBLISHED, "r3": None}))
        self.assertIn((SERVER, "R3 is not a package on nuget.org"), unknown)

    def test_registry_outage_fails_closed(self):
        Fixture(self.root)
        calls = []
        registry = fake_registry(fail=True)
        fetch = registry._fetch
        registry._fetch = lambda url: calls.append(url) or fetch(url)
        problems = self.problems(registry)
        self.assertEqual(1, len(calls))  # one retry cycle in total, not one per package
        self.assertEqual(10, len(problems))  # every pin of the three projects
        self.assertTrue(all("cannot verify on nuget.org" in m for _, m in problems))

    def test_msbuild_property_versions_are_resolved(self):
        fixture = Fixture(self.root)
        fixture.write(PLUGIN, textwrap.dedent("""\
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><RnVersion>$(RnBase)-dev.1</RnVersion><RnBase>5.5.1</RnBase></PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)' == 'Debug'"><RnVersion>5.5.0</RnVersion></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="com.IvanMurzak.ReflectorNet" Version="$(RnVersion)" />
                <PackageReference Include="R3"><Version>$(Missing)</Version></PackageReference>
              </ItemGroup>
            </Project>
            """))
        problems = self.problems()
        # A property set under different Conditions: every value it may take is checked.
        self.assertIn((PLUGIN, "com.IvanMurzak.ReflectorNet 5.5.1-dev.1 is a pre-release version"), problems)
        self.assertIn((PLUGIN, "R3: cannot resolve version '$(Missing)'"), problems)
        self.assertEqual(2, len(problems))

    def test_version_property_from_directory_build_props(self):
        fixture = Fixture(self.root)
        fixture.write("Directory.Build.props",
                      "<Project><PropertyGroup><ReflectorNetVersion>5.6.0-rc.2</ReflectorNetVersion></PropertyGroup></Project>")
        fixture.write(COMMON, csproj("$(ReflectorNetVersion)", reference=None))
        self.assertEqual([(COMMON, "com.IvanMurzak.ReflectorNet 5.6.0-rc.2 is a pre-release version")], self.problems())

    def test_central_package_versions_and_nuget_config_feed(self):
        fixture = Fixture(self.root)
        fixture.write("Directory.Packages.props",
                      '<Project><ItemGroup><PackageVersion Include="R3" Version="[1.4.0-beta]" />'
                      '</ItemGroup></Project>')
        fixture.write("nuget.config", '<configuration><packageSources><clear />'
                      '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
                      '<add key="pre" value="prerelease-packages" /></packageSources></configuration>')
        self.assertEqual([("Directory.Packages.props", "R3 1.4.0-beta is a pre-release version"),
                          ("nuget.config", "package source 'pre' is the local feed 'prerelease-packages'")],
                         self.problems())

    def test_nuspec_and_targets_in_a_packed_project_folder(self):
        fixture = Fixture(self.root)
        fixture.write("McpPlugin.Server/build/extra.targets",
                      '<Project><ItemGroup><PackageReference Include="R3" Version="1.4.0-beta" /></ItemGroup></Project>')
        fixture.write("McpPlugin.Server/McpPlugin.Server.nuspec",
                      '<package><metadata><dependencies><dependency id="R3" version="[2.0.0]" />'
                      '</dependencies></metadata></package>')
        self.assertEqual([("McpPlugin.Server/McpPlugin.Server.nuspec", "R3 2.0.0 is not published on nuget.org"),
                          ("McpPlugin.Server/build/extra.targets", "R3 1.4.0-beta is a pre-release version")],
                         self.problems())

    def test_floating_and_open_versions_are_not_pins(self):
        fixture = Fixture(self.root)
        fixture.write(COMMON, csproj("5.*", reference=None))
        fixture.write(PLUGIN, csproj("(,5.5.0]"))
        self.assertEqual([(COMMON, "com.IvanMurzak.ReflectorNet: floating version '5.*' is not a pin"),
                          (PLUGIN, "com.IvanMurzak.ReflectorNet: version range '(,5.5.0]' without a lower bound "
                                   "is not a pin")],
                         self.problems())

    def test_version_and_version_override_are_both_checked(self):
        fixture = Fixture(self.root)
        fixture.write(COMMON, csproj(reference=None, extra='\n    <PackageReference Include="ModelContextProtocol" '
                                                          'Version="1.5.0-dev" VersionOverride="1.4.0" />'))
        self.assertEqual([(COMMON, "ModelContextProtocol 1.5.0-dev is a pre-release version")], self.problems())

    def test_project_reference_to_an_unpublished_project_fails(self):
        fixture = Fixture(self.root)
        fixture.write(PLUGIN, csproj(reference=r"..\McpPlugin.NullEngine\McpPlugin.NullEngine.csproj"))
        self.assertEqual([(PLUGIN, r"references the unpublished project '..\McpPlugin.NullEngine\McpPlugin.NullEngine.csproj' "
                                   "(dotnet pack turns it into a NuGet dependency nobody publishes)")], self.problems())

    def test_restore_sources_property_with_url_passes(self):
        fixture = Fixture(self.root)
        fixture.write("Directory.Build.props", "<Project><PropertyGroup>"
                      "<RestoreSources>https://api.nuget.org/v3/index.json</RestoreSources></PropertyGroup></Project>")
        self.assertEqual([], self.problems())

    def test_restore_source_property_expanding_to_a_list(self):
        fixture = Fixture(self.root)
        fixture.write("Directory.Build.props", "<Project><PropertyGroup>"
                      "<Feeds>https://api.nuget.org/v3/index.json;./feed</Feeds>"
                      "<RestoreSources>$(Feeds)</RestoreSources></PropertyGroup></Project>")
        problems = self.problems()
        self.assertEqual(1, len(problems))
        self.assertIn("RestoreSources adds the local NuGet feed '$(Feeds)'", problems[0][1])

    def test_fallback_folders(self):
        fixture = Fixture(self.root)
        fixture.write("McpPlugin/nuget.config", '<configuration><fallbackPackageFolders>'
                      '<add key="f" value="./feed" /></fallbackPackageFolders></configuration>')
        fixture.write("Directory.Build.props", "<Project><PropertyGroup>"
                      "<RestoreAdditionalProjectFallbackFolders>C:\\packages</RestoreAdditionalProjectFallbackFolders>"
                      "</PropertyGroup></Project>")
        problems = self.problems()
        self.assertIn(("McpPlugin/nuget.config", "fallback folder 'f' is the local feed './feed'"), problems)
        self.assertTrue(any(f == "Directory.Build.props" and m.startswith("RestoreAdditionalProjectFallbackFolders adds the local")
                            for f, m in problems))
        self.assertEqual(2, len(problems))

    def test_missing_packed_project_fails(self):
        Fixture(self.root)
        self.root.joinpath(SERVER).unlink()
        self.assertEqual([(SERVER, "expected file is missing (update scripts/release_dependency_guard.py)")],
                         self.problems())

    def test_no_pins_found_fails(self):
        fixture = Fixture(self.root)
        fixture.write(COMMON, '<Project Sdk="Microsoft.NET.Sdk"></Project>')
        self.assertEqual([(COMMON, "no NuGet pins found (update scripts/release_dependency_guard.py)")], self.problems())

    def test_unknown_packable_project_fails(self):
        fixture = Fixture(self.root)
        fixture.write("McpPlugin.Extra/McpPlugin.Extra.csproj", csproj())
        fixture.write("McpPlugin.Extra/bin/Release/copy/McpPlugin.Extra.csproj", csproj())  # build output: ignored
        self.assertEqual([("McpPlugin.Extra/McpPlugin.Extra.csproj",
                           "packable project not scanned (add it to PACKED_PROJECTS in "
                           "scripts/release_dependency_guard.py)")], self.problems())

    def test_this_repository_layout_is_understood(self):
        """Offline: the real checkout has the three packed projects and the guard finds pins in each."""
        pins, problems = guard.collect(guard.ROOT)
        # A superset: under a chain lock, chain_feed.py adds a root Directory.Build.targets override pin.
        self.assertLessEqual({COMMON, PLUGIN, SERVER}, {guard.rel(p.file, guard.ROOT) for p in pins})
        self.assertFalse([p for p in problems if "update scripts/release_dependency_guard.py" in p.message], problems)


class RegistryTests(unittest.TestCase):
    """The real HTTP layer with urlopen patched: 404 = unknown package, anything else unreadable fails closed."""

    def fetch_with(self, *effects):
        with mock.patch.object(guard.urllib.request, "urlopen", side_effect=list(effects)) as urlopen, \
                mock.patch.object(guard.time, "sleep"):
            try:
                return guard._http_get("https://example.invalid/x/index.json"), urlopen.call_count
            except guard.RegistryError as error:
                return error, urlopen.call_count

    def test_404_is_unknown_package(self):
        error = urllib.error.HTTPError("u", 404, "Not Found", {}, None)
        self.assertEqual((None, 1), self.fetch_with(error))

    def test_server_errors_retry_then_fail(self):
        result, calls = self.fetch_with(urllib.error.HTTPError("u", 503, "Unavailable", {}, None),
                                        urllib.error.URLError("down"), TimeoutError())
        self.assertIsInstance(result, guard.RegistryError)
        self.assertEqual(3, calls)

    def test_malformed_body_fails_closed(self):
        for body in (b"<html>captive portal</html>", b"[]", b"{}"):
            registry = guard.NuGetRegistry(lambda url, body=body: body)
            with self.assertRaises(guard.RegistryError):
                registry.versions("R3")


class VersionTests(unittest.TestCase):
    def test_prerelease(self):
        self.assertTrue(guard.is_prerelease("5.5.1-dev.ec3702e"))
        self.assertTrue(guard.is_prerelease("1.0.0-rc.1+build"))
        self.assertFalse(guard.is_prerelease("1.0.0+build-7"))
        self.assertFalse(guard.is_prerelease("5.5.0"))

    def test_normalize(self):
        self.assertEqual("1.0.0", guard.normalize_nuget_version("1.0"))
        self.assertEqual("1.0.0", guard.normalize_nuget_version("1.0.0.0"))
        self.assertEqual("1.2.3.4", guard.normalize_nuget_version("01.2.3.4"))
        self.assertEqual("1.0.0-rc.1", guard.normalize_nuget_version("1.0.0-RC.1+meta"))

    def test_ranges(self):
        self.assertEqual((["1.0.0"], None), guard.nuget_versions_in("[1.0.0]"))
        self.assertEqual((["1.0"], None), guard.nuget_versions_in("[1.0,2.0)"))  # exclusive upper bound need not exist
        self.assertEqual((["1.0", "2.0"], None), guard.nuget_versions_in("[1.0,2.0]"))
        self.assertEqual(([], "floating version '1.*' is not a pin"), guard.nuget_versions_in("1.*"))
        self.assertEqual(([], "version range '(1.0,2.0)' with an exclusive lower bound is not a pin"),
                         guard.nuget_versions_in("(1.0,2.0)"))


if __name__ == "__main__":
    unittest.main()
