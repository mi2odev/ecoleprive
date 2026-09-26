#!/usr/bin/env python3
"""
Static checks WPF only reports at runtime:
  * every {StaticResource X} used in a XAML file is defined in App.xaml's dictionaries or earlier in the same file;
  * every view has a matching view model name (FooView <-> FooViewModel) for the ViewLocator convention.
Exits with status 1 when a problem is found.
"""
import pathlib, re, sys
repo = pathlib.Path(__file__).resolve().parent.parent
desk = repo / "src" / "CentreSoutien.Desktop"
pres = repo / "src" / "CentreSoutien.Presentation"

def keys(text):
    return set(re.findall(r'x:Key="([^"]+)"', text))

global_keys = set()
for f in ["Themes/Light.xaml", "Themes/Controls.xaml"]:
    global_keys |= keys((desk / f).read_text(encoding="utf-8"))
dark = keys((desk / "Themes/Dark.xaml").read_text(encoding="utf-8"))
light = keys((desk / "Themes/Light.xaml").read_text(encoding="utf-8"))
problems = []
if dark != light:
    problems.append(f"Light/Dark palettes differ: {sorted(dark ^ light)}")

for xaml in desk.rglob("*.xaml"):
    if "obj" in xaml.parts or "bin" in xaml.parts:
        continue
    text = xaml.read_text(encoding="utf-8")
    local = keys(text)
    for m in re.finditer(r'\{StaticResource\s+([^\s}]+)\}', text):
        key = m.group(1)
        if key.startswith("{x:Type"):
            continue
        if key not in global_keys and key not in local:
            line = text[:m.start()].count("\n") + 1
            problems.append(f"{xaml.relative_to(repo)}:{line}: unknown StaticResource '{key}'")
    for m in re.finditer(r'\{DynamicResource\s+([^\s}]+)\}', text):
        key = m.group(1)
        if key not in global_keys and key not in local:
            line = text[:m.start()].count("\n") + 1
            problems.append(f"{xaml.relative_to(repo)}:{line}: unknown DynamicResource '{key}'")

vms = {p.stem for p in pres.rglob("*.cs")}
vm_classes = set()
for p in pres.rglob("*.cs"):
    vm_classes |= set(re.findall(r'class (\w+ViewModel)\b', p.read_text(encoding="utf-8")))
views = {p.stem for p in (desk / "Views").rglob("*View.xaml")}
for v in sorted(views):
    if v + "Model" not in vm_classes:
        problems.append(f"view {v} has no view model {v}Model")
pages_and_dialogs = set()
for p in list((pres / "ViewModels" / "Pages").rglob("*.cs")) + list((pres / "ViewModels" / "Dialogs").rglob("*.cs")):
    pages_and_dialogs |= set(re.findall(r'public sealed (?:partial )?class (\w+ViewModel)\b', p.read_text(encoding="utf-8")))
pages_and_dialogs.add("ConfirmDialogViewModel")
missing = sorted(vm for vm in pages_and_dialogs if vm[:-len("Model")] not in views)
for vm in missing:
    problems.append(f"view model {vm} has no view {vm[:-5]}")

for p in problems:
    print(p)
print(f"{len(problems)} problem(s)")
sys.exit(1 if problems else 0)
