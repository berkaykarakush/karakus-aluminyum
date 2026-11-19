// Tüm ölçüler milimetredir: forma, hesaplara ve ekrana hep milimetre gider (Ercom gibi). Birim seçimi yoktur.
// Ölçü alanlarının (adı "Mm" ile biten veya data-mm taşıyan) yanına sabit "mm" etiketi eklenir.
window.Units = (() => {
  const format = (mm) => (Math.round(Number(mm) * 10) / 10).toLocaleString("tr-TR", { useGrouping: false, maximumFractionDigits: 1 });

  function enhance(input) {
    if (input.dataset.unitDone || input.type === "hidden") return;
    input.dataset.unitDone = "1";
    const wrap = document.createElement("span");
    wrap.className = "unit-field";
    const suffix = document.createElement("span");
    suffix.className = "unit-suffix";
    suffix.textContent = "mm";
    input.before(wrap);
    wrap.append(input, suffix);
  }
  const scan = (root) => {
    if (!root.querySelectorAll) return;
    root.querySelectorAll('input[name$="Mm"], input[data-mm]').forEach(enhance);
  };
  document.addEventListener("DOMContentLoaded", () => {
    scan(document);
    new MutationObserver((changes) => changes.forEach((change) => change.addedNodes.forEach(scan)))
      .observe(document.body, { childList: true, subtree: true });
  });

  return { code: "mm", format };
})();
