const stateKey = Symbol.for("claudex-yourself.hide-pets-button");
const previous = window[stateKey];
if (previous?.observer) previous.observer.disconnect();

const labels = new Set(["show pet", "hide pet"]);
const hideMatching = root => {
  const candidates = [];
  if (root instanceof Element && root.matches("[role='menuitem']")) candidates.push(root);
  if (root instanceof Element || root instanceof Document) {
    candidates.push(...root.querySelectorAll("[role='menuitem']"));
  }
  let hidden = 0;
  for (const element of candidates) {
    if (!labels.has(element.textContent?.trim().toLowerCase())) continue;
    element.style.setProperty("display", "none", "important");
    element.dataset.claudexHidePets = "true";
    hidden++;
  }
  return hidden;
};

let hiddenNow = hideMatching(document);
const observer = new MutationObserver(records => {
  for (const record of records) {
    for (const node of record.addedNodes) {
      if (node instanceof Element) hideMatching(node);
    }
  }
});
observer.observe(document.body, { childList: true, subtree: true });
window[stateKey] = { observer, hideMatching };
return { installed: true, hiddenNow };