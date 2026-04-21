#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use tauri::{Manager, PhysicalPosition, Position, WindowEvent};

#[tauri::command]
fn shell_ready() -> &'static str {
    "desktop-shell-ready"
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![shell_ready])
        .setup(|app| {
            if let Some(window) = app.get_webview_window("widget") {
                let _ = window.set_title("Desktop Companion Widget");
                if let (Ok(Some(monitor)), Ok(size)) = (window.current_monitor(), window.outer_size()) {
                    let work_area = monitor.work_area();
                    let x = work_area.position.x + work_area.size.width as i32 - size.width as i32 - 28;
                    let y = work_area.position.y + work_area.size.height as i32 - size.height as i32 - 36;
                    let _ = window.set_position(Position::Physical(PhysicalPosition::new(x, y)));
                }
            }
            Ok(())
        })
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .run(tauri::generate_context!())
        .expect("failed to run desktop shell");
}
