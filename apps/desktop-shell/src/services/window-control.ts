import { isTauriEnvironment } from "./shell-api";

export async function pinWindowLabel() {
  if (await isTauriEnvironment()) {
    return "tauri-window";
  }

  return "browser-preview";
}
