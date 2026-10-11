const assert = require("node:assert/strict");
const { evaluate } = require("./AutoloadWindowChecks.js");
async function exerciseIcons() {
  const check = (value, message) => { if (!value) throw new Error(message); };
  const controller = window[Symbol.for("claudex-yourself.remote-device-icons")];
  check(controller?.installed, "Device icon controller is installed");
  const device = controller.getDevices().find(device => device.os === "apple") || controller.getDevices()[0];
  check(device, "At least one remote device is available");
  const key = "claudex-yourself.remote-device-icons.v1", previous = localStorage.getItem(key);
  const imagesFor = () => [...document.querySelectorAll("svg[data-claudex-device-icon]")]
    .filter(svg => svg.dataset.claudexDeviceIcon === device.id)
    .map(svg => svg.parentElement.querySelector(".claudex-device-icon-image"));
  const waitFor = async (predicate, message) => {
    for (let attempt = 0; attempt < 40; attempt++) {
      if (predicate()) return;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw new Error(message);
  };
  try {
    for (const preset of controller.presets) {
      const decoded = new Image(); decoded.src = preset.src; await decoded.decode();
      controller.setChoice(device.id, preset.id);
      check(imagesFor().length > 0 && imagesFor().every(img => img?.getAttribute("src") === preset.src), `${preset.id} reaches all native and grouped rows`);
    }
    controller.setChoice(device.id, "native");
    check(imagesFor().length === 0, "Default globe removes replacements for this device");
    const currentRow = () => [...document.querySelectorAll(".claudex-device-option")].find(row => row.dataset.deviceId === device.id);
    const row = currentRow();
    check(row, "Per-device settings controls are mounted");
    const picker = row.querySelector("input[type=file]");
    const files = new DataTransfer();
    files.items.add(new File(['<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><circle cx="16" cy="16" r="14" fill="red"/></svg>'], "test-icon.svg", {type:"image/svg+xml"}));
    picker.files = files.files; picker.dispatchEvent(new Event("change", {bubbles:true}));
    await waitFor(() => JSON.parse(localStorage.getItem(key))?.[device.id]?.choice === "custom", "File input completes upload");
    const saved = JSON.parse(localStorage.getItem(key))[device.id];
    check(saved.choice === "custom" && saved.customData.startsWith("data:image/png;base64,"), "File input stores a normalized PNG");
    await waitFor(() => imagesFor().length > 0 && imagesFor().every(img => img?.getAttribute("src") === saved.customData), "Uploaded icon reaches rendered rows");
    const bad = new DataTransfer(); bad.items.add(new File(["no"], "test.html", {type:"text/html"}));
    const currentPicker = currentRow().querySelector("input[type=file]");
    currentPicker.files = bad.files; currentPicker.dispatchEvent(new Event("change", {bubbles:true}));
    await new Promise(resolve => setTimeout(resolve, 50));
    check(currentRow().querySelector("[role=status]").textContent.includes("PNG"), "Invalid uploads show an inline error");
    controller.setChoice(device.id, "duck");
    controller.uninstall();
    check(document.querySelectorAll(".claudex-device-icon-image,svg[data-claudex-device-icon]").length === 0, "Uninstall restores native SVGs and removes all overlays");
    check(getComputedStyle(currentRow()).display === "flex", "Settings remain styled while disabled");
    controller.install();
    check(imagesFor().length > 0, "Reinstall restores saved choices");
    return { presets: controller.presets.length, filePicker: true, invalidUpload: true, reversible: true, devices: controller.getDevices().length };
  } finally {
    controller.setChoice(device.id, "auto");
    if (previous === null) localStorage.removeItem(key); else localStorage.setItem(key, previous);
    window.dispatchEvent(new StorageEvent("storage", {key, newValue:previous}));
  }
}
async function main() {
  const targets = await (await fetch("http://127.0.0.1:9229/json/list")).json();
  const target = targets.find(target => target.url === "app://-/index.html");
  assert(target, "Controlled main renderer is available");
  const result = await evaluate(target, `(${exerciseIcons.toString()})()`);
  console.log("Remote device icon live checks passed:", JSON.stringify(result));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
