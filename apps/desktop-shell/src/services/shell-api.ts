export async function isTauriEnvironment() {
  return "__TAURI_INTERNALS__" in window;
}
