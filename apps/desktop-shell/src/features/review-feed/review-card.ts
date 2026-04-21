import type { ReviewDigest } from "@desktop-companion/shared-types";

export function createReviewCard(review: ReviewDigest) {
  const section = document.createElement("section");
  section.className = "panel-card";
  section.innerHTML = `
    <div class="panel-head">
      <h3>阶段梳理</h3>
      <span>${new Date(review.createdAt).toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })}</span>
    </div>
    <strong class="review-headline">${review.headline}</strong>
    <p class="review-summary">${review.summary}</p>
    <ul class="review-list">
      ${review.bullets.map((item) => `<li>${item}</li>`).join("")}
    </ul>
  `;

  return section;
}
