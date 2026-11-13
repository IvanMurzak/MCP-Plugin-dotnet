"""Regenerate the README images: py -3 docs/assets/build-svgs.py (from the repository root or anywhere).

Uses svgkit.py (the shared ai-game.dev visual style) from this folder.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True  # keep docs/assets free of __pycache__
sys.path.insert(0, HERE)
import svgkit as K  # noqa: E402


def text(x, y, s, size=16, fill=K.MUTED, anchor="start", mono=False, weight=None):
    w = f' font-weight="{weight}"' if weight else ""
    return (f'<text x="{x}" y="{y}" text-anchor="{anchor}" class="{"m" if mono else "s"}" '
            f'font-size="{size}" fill="{fill}"{w}>{K.esc(s)}</text>')


# ---- hero: agent <- server <- app on the right --------------------------------------------------
right = (
    K.card(800, 40, 340, 70, "AI agent", "any MCP client", accent="red")
    + K.card(800, 155, 340, 70, "McpPlugin.Server", "the bridge (ASP.NET Core)", accent="blue", mono_title=True)
    + K.logo(1102, 190, 46)
    + K.card(800, 270, 340, 70, "Your .NET app", "+ McpPlugin", accent="green")
    + K.wire("M970 155 L970 110", color=K.RED_TEXT, dur=1.6)
    + K.wire("M970 270 L970 225", color=K.BLUE, dur=1.6, begin=0.8)
    + text(988, 139, "MCP: stdio or HTTP")
    + text(988, 254, "SignalR")
)
K.write(os.path.join(HERE, "hero.svg"), K.hero(
    title="MCP Plugin for ", accent=".NET", eyebrow_text="Model Context Protocol",
    subtitle=["Expose tools, prompts and resources of a", "running .NET app to AI agents."],
    chip_items=["netstandard2.1", "net8.0", "net9.0", "SignalR"], right=right,
    label="MCP Plugin for .NET: a running .NET app connects to McpPlugin.Server over SignalR, "
          "and AI agents reach it over MCP"))

# ---- architecture ---------------------------------------------------------------------------------
W, H = 1200, 540
body = K.eyebrow(24, 64, "Your .NET apps") + K.eyebrow(380, 64, "The bridge") + K.eyebrow(916, 64, "Callers")

apps = [("Game editor plugin", "McpPlugin · role: editor"),
        ("Game build", "McpPlugin · role: runtime"),
        ("Any .NET app", "desktop, console, server")]
for i, (t, s) in enumerate(apps):
    y = 96 + i * 126
    sy = 200 + i * 60  # each wire meets the server at its own point
    body += K.wire(f"M380 {sy} C332 {sy} 332 {y + 38} 284 {y + 38}", color=K.BLUE, dur=2.0, begin=i * 0.6)
    body += K.card(24, y, 260, 76, t, s, accent="blue")

callers = [("Local AI agent", "launches the server (stdio)", "red"),
           ("Remote AI agent", "streamable HTTP on /mcp", "red"),
           ("MCP Inspector", "debug tools and resources", "yellow"),
           ("Scripts and tests", "REST: /api/tools/{name}", "green")]
for i, (t, s, a) in enumerate(callers):
    y = 96 + i * 86
    sy = 170 + i * 60
    body += K.wire(f"M916 {y + 35} C868 {y + 35} 868 {sy} 820 {sy}", color=K.ACCENTS[a], dur=2.4, begin=i * 0.6)
    body += K.card(916, y, 264, 70, t, s, accent=a)

body += text(332, 112, "SignalR", anchor="middle", fill=K.SECOND)
body += text(868, 112, "MCP · REST", anchor="middle", fill=K.SECOND)

# server box
body += (f'<rect x="380" y="96" width="440" height="328" rx="14" fill="url(#card)" stroke="{K.HAIR_STRONG}"/>'
         + K.logo(416, 127, 28)
         + text(438, 134, "McpPlugin.Server", size=19, fill=K.TEXT, mono=True, weight=700)
         + text(798, 134, "ASP.NET Core", anchor="end"))
parts = ["SignalR hub", "Version check", "Tool router", "Prompts, resources",
         "Auth modes", "Webhooks", "Role routing", "Reconnect grace"]
for i, p in enumerate(parts):
    body += K.pill(400 + (i % 2) * 204, 156 + (i // 2) * 46, p, mono=False, color=K.TEXT, w=196)[0]
body += (f'<rect x="400" y="346" width="400" height="62" rx="12" fill="{K.SURF}" stroke="{K.HAIR}"/>'
         + text(418, 371, "/hub/mcp-server · /mcp · /api/tools", fill=K.BLUE, mono=True)
         + text(418, 396, "SignalR: apps · MCP: agents · REST: scripts"))

body += text(W / 2, 488, "McpPlugin: netstandard2.1 · net8.0 · net9.0   ·   McpPlugin.Server: net8.0 · net9.0"
             "   ·   built on ReflectorNet", anchor="middle")

K.write(os.path.join(HERE, "architecture.svg"), K.panel(
    W, H, body, "Architecture: .NET apps with McpPlugin connect to McpPlugin.Server over SignalR; AI agents and "
    "scripts reach the server over MCP (stdio or streamable HTTP) and REST"))
