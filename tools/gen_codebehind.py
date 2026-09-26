#!/usr/bin/env python3
"""Creates the minimal code-behind (.xaml.cs) for every view XAML that lacks one."""
import pathlib, re, sys
root = pathlib.Path(__file__).resolve().parent.parent / "src" / "CentreSoutien.Desktop" / "Views"
for xaml in root.rglob("*.xaml"):
    cs = xaml.with_suffix(".xaml.cs")
    if cs.exists():
        continue
    text = xaml.read_text(encoding="utf-8")
    m = re.search(r'x:Class="([\w.]+)\.(\w+)"', text)
    root_tag = re.match(r'\s*<(\w+)', text).group(1)
    ns, cls = m.group(1), m.group(2)
    cs.write_text(f"""namespace {ns};

public partial class {cls}
{{
    public {cls}() => InitializeComponent();
}}
""", encoding="utf-8")
    print("created", cs.relative_to(root))
