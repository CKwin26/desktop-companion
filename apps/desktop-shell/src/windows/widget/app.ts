import { createPetAvatar } from "../../components/pet-avatar/pet-avatar";
import { getShellViewModel } from "../../stores/companion-store";

export function createShellApp() {
  const app = document.createElement("main");
  app.className = "desktop-pet-root";
  app.innerHTML = `<p class="pet-loading">正在叫醒桌面伴侣...</p>`;

  void render(app);
  return app;
}

async function render(root: HTMLElement) {
  const viewModel = await getShellViewModel();

  root.innerHTML = "";
  const bubble = document.createElement("section");
  bubble.className = "speech-cloud";
  bubble.setAttribute("data-tauri-drag-region", "");
  bubble.innerHTML = `
    <span class="speech-label">桌面监督中</span>
    <strong class="speech-title">${viewModel.topTask?.title ?? "先交给我今天的第一件事"}</strong>
    <p class="speech-line">${viewModel.statusLine}</p>
    <div class="speech-meta">
      <span>${viewModel.taskCount} 个任务</span>
      <span>${viewModel.blockedCount} 个阻塞</span>
      <span>${viewModel.doneCount} 个完成</span>
    </div>
  `;

  const whisper = document.createElement("p");
  whisper.className = "pet-whisper";
  whisper.setAttribute("data-tauri-drag-region", "");
  whisper.textContent = viewModel.reply;

  root.append(bubble);
  root.append(createPetAvatar(viewModel.petName, viewModel.emotion));
  root.append(whisper);
}
