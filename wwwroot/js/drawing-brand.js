// Editörde seçilen markayı çizimin sağ üstünde gösterir.
(() => {
  const select = document.querySelector("[data-brand-select]"), mark = document.getElementById("wd-brand-mark");
  if (!select || !mark) return;
  function show() {
    const option = select.selectedOptions[0];
    mark.textContent = "";
    mark.hidden = !select.value;
    if (!select.value) return;
    if (option.dataset.logo) {
      const image = document.createElement("img");
      image.src = option.dataset.logo; image.alt = option.textContent;
      mark.appendChild(image);
    } else mark.textContent = option.textContent;
  }
  select.addEventListener("change", show);
  show();
})();
