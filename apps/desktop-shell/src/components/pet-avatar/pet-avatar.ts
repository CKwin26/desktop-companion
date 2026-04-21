import type { EmotionSnapshot } from "@desktop-companion/shared-types";

export function createPetAvatar(petName: string, emotion: EmotionSnapshot) {
  const shell = document.createElement("section");
  shell.className = "pet-shell";
  shell.setAttribute("data-tauri-drag-region", "");
  shell.innerHTML = `
    <div class="pet-orbit pet-orbit-left" aria-hidden="true"></div>
    <div class="pet-orbit pet-orbit-right" aria-hidden="true"></div>
    <div class="pet-name-tag">${petName}</div>
    <div class="pet-stage">
      <div class="pet-ears" aria-hidden="true">
        <span class="pet-ear pet-ear-left"></span>
        <span class="pet-ear pet-ear-right"></span>
      </div>
      <div class="pet-body pet-${emotion.name}">
        <div class="pet-face">
          <span class="pet-eye pet-eye-left"></span>
          <span class="pet-eye pet-eye-right"></span>
          <span class="pet-mouth"></span>
        </div>
        <span class="pet-blush pet-blush-left"></span>
        <span class="pet-blush pet-blush-right"></span>
        <div class="pet-apron" aria-hidden="true"></div>
      </div>
      <div class="pet-shadow" aria-hidden="true"></div>
    </div>
  `;

  return shell;
}
