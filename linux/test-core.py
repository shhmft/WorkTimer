#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Проверка ядра linux-версии без GTK: подменяем gi заглушками."""
import sys, os, types, datetime as dt, importlib.util, tempfile
import importlib.machinery

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
print("7. независимые работы и совместимость с Windows")
os.environ["XDG_DATA_HOME"] = os.path.join(tmp, "parallel-data")
os.environ["XDG_CONFIG_HOME"] = os.path.join(tmp, "parallel-cfg")
with open(wt.SESSIONS(), "w", encoding="utf-8-sig") as f:
    f.write("%s|%s\n" % (base.isoformat(), (base + dt.timedelta(hours=1)).isoformat()))
with open(wt.ADJUST(), "w", encoding="utf-8-sig") as f:
    f.write("%s|15\n" % d.isoformat())
c = wt.Core()
first = c.current
check("старые данные в общей работе", wt.fmt(c.today()), "1:15")
second = c.add_project('Проект; "Дом" | фасад')
c.toggle(first)
first.run_start = dt.datetime.now() - dt.timedelta(minutes=10)
c.toggle(second)
second.run_start = dt.datetime.now() - dt.timedelta(minutes=20)
check("два таймера запущены", c.running_count, 2)
check("часы первой работы", wt.fmt(c.day_map(first)[d]), "1:25")
check("часы второй работы", wt.fmt(c.today()), "0:20")
c.select(first)
check("смена работы не останавливает таймеры", c.running_count, 2)
c.save(); c.save()
check("автосохранение без дублей", len(wt.load_segments()), 3)
c.toggle(first)
check("пауза одной не мешает второй", second.run_start is not None, True)
c.stop()
check("стоп сбрасывает только свою сессию", first.session_accum, 0.0)
check("стоп сохраняет часы", wt.fmt(c.today()), "1:25")
c.last_save = dt.datetime.now() - dt.timedelta(seconds=20)
fake_app = object.__new__(wt.App)
fake_app.core = c
fake_app.refresh = lambda: None
fake_app.tick()
check("фоновая работа автосохраняется", (dt.datetime.now() - c.last_save).total_seconds() < 1, True)
c.select(second)
c.toggle()
c.adjust[d] = 30
check("поправка второй работы", wt.fmt(c.today()), "0:50")
check("поправка не затрагивает первую", wt.fmt(c.day_map(first)[d]), "1:25")
night = base.replace(hour=23) - dt.timedelta(days=2)
c.segments.append((night, night + dt.timedelta(hours=3), second.id))
check("ночная работа до полуночи", wt.fmt(c.day_map()[d - dt.timedelta(days=2)]), "1:00")
check("ночная работа после полуночи", wt.fmt(c.day_map()[d - dt.timedelta(days=1)]), "2:00")
check("ночная работа изолирована", d - dt.timedelta(days=1) in c.day_map(first), False)
exported = c.export_csv(d - dt.timedelta(days=2), d)
check("CSV экранирует название", '"Проект; ""Дом"" | фасад"' in exported, True)
check("CSV итог выбранной работы", 'ИТОГО;;;3:50;3,83' in exported, True)
check("CSV ограничен периодом", (d - dt.timedelta(days=1)).strftime("%d.%m.%Y") in c.export_csv(d, d), False)
c.rename_project("Дом на берегу")
check("переименование сохраняет часы", wt.fmt(c.today()), "0:50")
c.save()
restored = wt.Core()
check("выбор работы восстановлен", restored.current.id, second.id)
check("название восстановлено", restored.current.name, "Дом на берегу")
check("часы восстановлены", wt.fmt(restored.today()), "0:50")
check("поправка общей работы восстановлена", restored.projects[0].adjust[d], 15.0)
check("после перезапуска счёт стоит", restored.any_running, False)
for name in ("", " " * 2, "Общая работа", "a" * 101, "a\nb"):
    try:
        c.add_project(name)
        rejected = False
    except ValueError:
        rejected = True
    check("недопустимое название %r" % name[:20], rejected, True)
c.toggle(first); c.toggle(second)
c.pause_all()
check("пауза всех работ", c.running_count, 0)
# Windows writes ISO datetimes with offsets and seven fractional digits.
with open(wt.SESSIONS(), "w", encoding="utf-8-sig") as f:
    f.write("%sT08:00:00.1234567+03:00|%sT09:00:00.1234567+03:00|default\n" % (d, d))
check("ISO Windows читается в Linux", wt.fmt(wt.Core().day_map(first)[d]), "1:15")

print("итого: %d успешно, %d провалено" % (ok, fail))
sys.exit(1 if fail else 0)
