import "./styles/global.css";
import { createShellApp } from "./windows/widget/app";

const root = document.querySelector<HTMLDivElement>("#app");

if (!root) {
  throw new Error("App root not found.");
}

root.innerHTML = "";
root.append(createShellApp());
