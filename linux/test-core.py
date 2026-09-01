#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Проверка ядра linux-версии без GTK: подменяем gi заглушками."""
import sys, os, types, datetime as dt, importlib.util, tempfile

# --- заглушки вместо графики ---
gi = types.ModuleType("gi")
gi.require_version = lambda *a, **k: None
repo = types.ModuleType("gi.repository")
for name in ("Gtk", "Gdk", "GLib", "Pango", "PangoCairo"):
    m = types.ModuleType("gi.repository." + name)
    m.__getattr__ = lambda n: object
    setattr(repo, name, m)
    sys.modules["gi.repository." + name] = m
gi.repository = repo
sys.modules["gi"] = gi
sys.modules["gi.repository"] = repo


class Win:
    def __init__(self, *a, **k): pass
    def __init_subclass__(cls, **k): pass


repo.Gtk.Window = Win

tmp = tempfile.mkdtemp()
os.environ["XDG_DATA_HOME"] = os.path.join(tmp, "data")
os.environ["XDG_CONFIG_HOME"] = os.path.join(tmp, "cfg")

here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_loader(
    "wt", importlib.machinery.SourceFileLoader("wt", os.path.join(here, "worktimer")))
wt = importlib.util.module_from_spec(spec)
spec.loader.exec_module(wt)

ok = fail = 0


def check(name, got, want):
    global ok, fail
    if got == want:
        ok += 1
        print("  ok   %-46s %s" % (name, got))
    else:
        fail += 1
        print("  ФЕЙЛ %-46s получено %r, ждали %r" % (name, got, want))


print("1. разбор ввода часов")
check("7:30", wt.parse_hours("7:30"), 450)
check("7.5", wt.parse_hours("7.5"), 450.0)
check("7,5 (запятая)", wt.parse_hours("7,5"), 450.0)
check("450m", wt.parse_hours("450m"), 450.0)
check("мусор", wt.parse_hours("абв"), None)

print("2. формат вывода")
check("125 минут", wt.fmt(125), "2:05")
check("отрицательное", wt.fmt(-5), "0:00")
check("округление 59.7", wt.fmt(59.7), "1:00")

print("3. учёт времени и поправки")
core = wt.Core()
d = dt.date.today()
base = dt.datetime.combine(d, dt.time(10, 0))
core.segments.append((base, base + dt.timedelta(minutes=120)))
check("два часа отсчитано", wt.fmt(core.today()), "2:00")

core.adjust[d] = 60          # ручная правка: +1 час
check("после правки +1 ч", wt.fmt(core.today()), "3:00")

core.segments.append((base + dt.timedelta(hours=5),
                      base + dt.timedelta(hours=5, minutes=30)))
check("учёт продолжается поверх правки", wt.fmt(core.today()), "3:30")

print("4. переход через полночь")
c2 = wt.Core()
c2.segments.clear()
night = dt.datetime.combine(d, dt.time(23, 0))
c2.segments.append((night, night + dt.timedelta(hours=3)))
m = c2.day_map()
check("вечер отходит первому дню", wt.fmt(m.get(d, 0)), "1:00")
check("остаток — следующему", wt.fmt(m.get(d + dt.timedelta(days=1), 0)), "2:00")

print("5. запись и чтение с диска")
c3 = wt.Core()
c3.segments = list(core.segments)
c3.adjust = dict(core.adjust)
c3.save()
c4 = wt.Core()
check("отрезки перечитаны", len(c4.segments), len(c3.segments))
check("поправки перечитаны", c4.adjust, c3.adjust)
check("сумма совпала", wt.fmt(c4.today()), wt.fmt(c3.today()))
check("ошибок записи нет", c3.save_broken, False)

print("6. атомарность записи")
p = os.path.join(wt.data_dir(), "probe.txt")
wt.write_atomic(p, "первое")
wt.write_atomic(p, "второе")
check("перезапись поверх", open(p, encoding="utf-8").read(), "второе")
check("временный файл убран", os.path.exists(p + ".tmp"), False)

print()
print("итого: %d успешно, %d провалено" % (ok, fail))
sys.exit(1 if fail else 0)
