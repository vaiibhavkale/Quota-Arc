"""Convert Sources/Providers/GlyphOutline.swift into C# GlyphOutline.cs."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "Sources" / "Providers" / "GlyphOutline.swift"
DST = ROOT / "windows" / "QuotaArc" / "Providers" / "GlyphOutline.cs"

POINT = re.compile(r"CGPoint\(x:\s*([-\d.]+),\s*y:\s*([-\d.]+)\)")
STATIC = re.compile(r"static let (\w+): \[\[CGPoint\]\] = \[")


def parse(text: str) -> dict[str, list[list[tuple[float, float]]]]:
    glyphs: dict[str, list[list[tuple[float, float]]]] = {}
    pos = 0
    while True:
        m = STATIC.search(text, pos)
        if not m:
            break
        name = m.group(1)
        start = m.end() - 1
        depth = 0
        i = start
        while i < len(text):
            if text[i] == "[":
                depth += 1
            elif text[i] == "]":
                depth -= 1
                if depth == 0:
                    block = text[start : i + 1]
                    glyphs[name] = parse_loops(block)
                    pos = i + 1
                    break
            i += 1
        else:
            break
    return glyphs


def parse_loops(block: str) -> list[list[tuple[float, float]]]:
    loops: list[list[tuple[float, float]]] = []
    # Split on "], [" between loops at the outer-inner level is messy; parse
    # bracketed groups that contain CGPoints.
    inner = block.strip()
    if inner.startswith("["):
        inner = inner[1:-1]
    current: list[tuple[float, float]] = []
    depth = 0
    buf = ""
    for ch in inner:
        if ch == "[":
            depth += 1
            if depth == 1:
                buf = ""
                continue
        if ch == "]":
            depth -= 1
            if depth == 0:
                pts = [(float(x), float(y)) for x, y in POINT.findall(buf)]
                if pts:
                    loops.append(pts)
                buf = ""
                continue
        if depth >= 1:
            buf += ch
    return loops


def emit(glyphs: dict[str, list[list[tuple[float, float]]]]) -> str:
    names = ["claude", "openai", "third", "cursor", "gemini", "antigravity", "glm", "grok", "opencode"]
    chunks = [
        "// Converted from Sources/Providers/GlyphOutline.swift. Do not edit by hand;",
        "// regenerate with windows/tools/convert_glyphs.py.",
        "using System.Windows;",
        "",
        "namespace QuotaArc.Providers;",
        "",
        "internal static class GlyphOutline",
        "{",
    ]
    for name in names:
        loops = glyphs[name]
        chunks.append(f"    public static readonly Point[][] {Pascal(name)} =")
        chunks.append("    [")
        for loop in loops:
            chunks.append("        [")
            line: list[str] = []
            for i, (x, y) in enumerate(loop):
                line.append(f"new({x:.4f}, {y:.4f})")
                if len(line) == 3 or i == len(loop) - 1:
                    chunks.append("            " + ", ".join(line) + ("," if i != len(loop) - 1 else ""))
                    line = []
            chunks.append("        ],")
        chunks.append("    ];")
        chunks.append("")
    chunks.append("}")
    chunks.append("")
    return "\n".join(chunks)


def Pascal(name: str) -> str:
    return name[0].upper() + name[1:]


def main() -> None:
    glyphs = parse(SRC.read_text(encoding="utf-8"))
    missing = [n for n in ["claude", "openai", "third", "cursor", "gemini", "antigravity", "glm", "grok", "opencode"] if n not in glyphs]
    if missing:
        raise SystemExit(f"missing glyphs: {missing}")
    DST.parent.mkdir(parents=True, exist_ok=True)
    DST.write_text(emit(glyphs), encoding="utf-8")
    print(f"wrote {DST} ({sum(len(p) for g in glyphs.values() for p in g)} points)")


if __name__ == "__main__":
    main()
