import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./design-system/tokens.css";
import "./shell/shell.css";
import { App } from "./shell/App";

document.documentElement.dataset.theme = localStorage.getItem("astra-theme") ?? "dark";
createRoot(document.getElementById("root")!).render(<StrictMode><App /></StrictMode>);
