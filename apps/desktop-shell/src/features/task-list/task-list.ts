import type { Task } from "@desktop-companion/shared-types";

export function createTaskList(tasks: Task[]) {
  const section = document.createElement("section");
  section.className = "panel-card";

  const items = tasks
    .map(
      (task) => `
        <li class="task-item">
          <strong>${task.title}</strong>
          <span>${task.status} · ${task.priority}</span>
        </li>
      `
    )
    .join("");

  section.innerHTML = `
    <div class="panel-head">
      <h3>当前任务</h3>
      <span>${tasks.length} 件</span>
    </div>
    <ul class="task-list">${items}</ul>
  `;

  return section;
}
