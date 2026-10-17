#!/usr/bin/env python3
"""Exercise real GTK project controls and rendering under xvfb."""
import datetime as dt
import importlib.machinery
import importlib.util
import os
import pathlib
import sys
import tempfile
import types

import cairo
import gi
gi.require_version("Gtk", "3.0")
from gi.repository import Gtk, Gdk, GLib

with tempfile.TemporaryDirectory(prefix="worktimer-gui-") as folder:
    os.environ["XDG_DATA_HOME"] = folder + "/data"
    os.environ["XDG_CONFIG_HOME"] = folder + "/config"
    path = pathlib.Path(__file__).with_name("worktimer")
    spec = importlib.util.spec_from_loader("wt", importlib.machinery.SourceFileLoader("wt", str(path)))
    wt = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(wt)
    wt.save_settings({"widget": "0", "updates": "0"})
    app = wt.App()
    checks = 0

    def check(value):
        global checks
        assert value
        checks += 1

    def answer_dialog(name):
        for window in Gtk.Window.list_toplevels():
            if isinstance(window, Gtk.Dialog) and window.get_visible():
                for child in window.get_content_area().get_children():
                    if isinstance(child, Gtk.Entry):
                        child.set_text(name)
                        window.response(Gtk.ResponseType.OK)
                        return False
        return True

    GLib.idle_add(answer_dialog, "Дом на берегу")
    app.project_dialog()
    check(len(app.core.projects) == 2 and app.core.current.name == "Дом на берегу")
    first, second = app.core.projects
    app.core.toggle(first)
    app.core.select(second)
    # Click the second row's play/pause button.
    event = types.SimpleNamespace(x=wt.W + 216, y=156 + 76 + 30,
                                  type=Gdk.EventType.BUTTON_PRESS)
    app.window.on_click(None, event)
    check(app.core.running_count == 2)
    # Async release check updates the menu without pausing timers or using the network.
    original_check = wt.updates.check_release
    wt.updates.check_release = lambda: {"version": "1.4.0"}
    app.confirm_update = lambda _message: False
    app.check_updates(True)
    deadline = dt.datetime.now() + dt.timedelta(seconds=3)
    while app.update_busy and dt.datetime.now() < deadline:
        Gtk.main_iteration_do(False)
    check(not app.update_busy and app.mi_update.get_label() == "Обновить до 1.4.0")
    check(app.core.running_count == 2)
    def click_manual_setting(_unused=None):
        for window in Gtk.Window.list_toplevels():
            if isinstance(window, Gtk.Dialog) and window.get_title() == "Настройки":
                for button in window.get_action_area().get_children():
                    if isinstance(button, Gtk.Button) and button.get_label() == "Проверить обновления":
                        button.clicked()
                        return False
        return True
    GLib.idle_add(click_manual_setting)
    app.settings_dialog()
    deadline = dt.datetime.now() + dt.timedelta(seconds=3)
    while app.update_busy and dt.datetime.now() < deadline:
        Gtk.main_iteration_do(False)
    check(not app.update_busy and app.mi_update.get_label() == "Обновить до 1.4.0")
    wt.updates.check_release = original_check
    # Click the first row's name: both timers must continue.
    event.x, event.y = wt.W + 30, 180
    app.window.on_click(None, event)
    check(app.core.current is first and app.core.running_count == 2)
    event.x = wt.W + 216
    app.window.on_click(None, event)
    check(first.run_start is None and second.run_start is not None)
    app.core.select(second)
    GLib.idle_add(answer_dialog, "Фасад дома")
    app.project_dialog(rename=True)
    check(second.name == "Фасад дома" and second.run_start is not None)
    app.refresh_project_menu()
    check(len(app.project_menu.get_children()) == 2)
    app.toggle_widget()
    check(app.widget is not None)
    widget_running = app.core.running_count
    app.widget.select_project(first)
    check(app.core.current is first and app.core.running_count == widget_running)
    app.widget.select_project(second)
    check(app.core.current is second and app.core.running_count == widget_running)
    for index in range(8):
        app.core.add_project("Работа %d" % index)
    app.core.select(first)
    picker = wt.ProjectPicker(app.widget)
    app.widget.project_picker = picker
    check(picker.rows == picker.MAX_ROWS and picker.first == 0)
    picker.on_scroll(None, types.SimpleNamespace(direction=Gdk.ScrollDirection.DOWN))
    check(picker.first == 1)
    picker.on_press(None, types.SimpleNamespace(button=1, y=picker.PAD + 1))
    check(app.core.current is second and app.core.running_count == widget_running)
    app.core.select(app.core.projects[-1])
    picker = wt.ProjectPicker(app.widget)
    app.widget.project_picker = picker
    check(picker.first == len(app.core.projects) - picker.rows)
    picker.show_picker(types.SimpleNamespace(x_root=60, y_root=60))
    while Gtk.events_pending():
        Gtk.main_iteration_do(False)
    check(picker.get_visible())
    popup_surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, 214, picker.PAD * 2 + picker.rows * picker.ROW_H)
    picker.on_draw(picker, cairo.Context(popup_surface))
    picker.on_key(None, types.SimpleNamespace(keyval=Gdk.KEY_Home))
    check(picker.first == 0 and picker.hot == 0)
    picker.on_key(None, types.SimpleNamespace(keyval=Gdk.KEY_Return))
    check(app.core.current is first and app.core.running_count == widget_running)
    check(app.widget.project_picker is None)
    picker = wt.ProjectPicker(app.widget)
    app.widget.project_picker = picker
    picker.destroy()
    check(app.widget.project_picker is None)
    temporary_widget = wt.Widget(app)
    picker = wt.ProjectPicker(temporary_widget)
    temporary_widget.project_picker = picker
    picker.show_all()
    temporary_widget.destroy()
    check(temporary_widget.project_picker is None)
    app.core.projects[:] = [first, second]
    app.core.select(second)
    first.run_start = dt.datetime.now() - dt.timedelta(minutes=20)
    second.run_start = dt.datetime.now() - dt.timedelta(minutes=55)
    app.show_window()
    while Gtk.events_pending():
        Gtk.main_iteration_do(False)
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, wt.TOTAL_W, wt.H)
    app.window.on_draw(app.window.area, cairo.Context(surface))
    check(len(app.window.rows) > 0)
    if len(sys.argv) > 1:
        surface.write_to_png(sys.argv[1])
    app.core.pause_all()
    app.refresh()
    check(not app.mi_pause_all.get_sensitive())
    app.widget.destroy()
    app.window.destroy()
    print("PASS: %d real GTK checks" % checks)
