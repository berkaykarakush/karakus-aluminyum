// Panjur, korkuluk ve cam balkon editörü: birkaç seçim yeterlidir, şekil kendini kurar.
(() => {
  const form = document.getElementById("pd-form");
  if (!form) return;
  const svg = document.getElementById("pd-canvas");
  const field = (name) => form.elements[name];
  const num = (name) => Number(field(name).value) || 0;
  const params = () => {
    const out = {};
    form.querySelectorAll("[data-param]").forEach((input) => { out[input.dataset.param] = input.type === "number" ? Number(input.value) || 0 : input.value; });
    return out;
  };

  function refresh() {
    field("ParamsJson").value = JSON.stringify(params());
    WindowDrawing.render(svg, { category: field("Category").value, widthMm: num("WidthMm"), heightMm: num("HeightMm"), colorHex: field("ColorHex").value, params: params() });
  }

  // Kayıtlı seçimleri forma yükle
  const saved = JSON.parse(field("ParamsJson").value || "{}");
  form.querySelectorAll("[data-param]").forEach((input) => {
    if (saved[input.dataset.param] !== undefined && saved[input.dataset.param] !== null) input.value = saved[input.dataset.param];
  });
  form.addEventListener("input", refresh);
  form.addEventListener("change", refresh);
  document.querySelectorAll("[data-color]").forEach((button) => button.addEventListener("click", () => { field("ColorHex").value = button.dataset.color; refresh(); }));
  document.addEventListener("units:change", refresh);
  form.addEventListener("submit", () => { field("ParamsJson").value = JSON.stringify(params()); });
  refresh();
})();
