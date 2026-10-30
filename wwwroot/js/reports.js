// Rapor grafiği: üzerine gelince dönemi izleyen çizgi ve o dönemin tüm değerleri. İçerik textContent ile yazılır.
(() => {
  const money = (v) => Number(v).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " TL";
  document.querySelectorAll("[data-report-chart]").forEach((box) => {
    const data = JSON.parse(box.dataset.reportChart);
    const svg = box.querySelector("svg"), cross = box.querySelector(".report-cross"), tip = box.querySelector(".report-tip");
    let index = data.labels.length - 1;

    function show(i) {
      index = Math.max(0, Math.min(data.labels.length - 1, i));
      const x = data.x[index];
      cross.setAttribute("x1", x); cross.setAttribute("x2", x); cross.hidden = false;
      tip.textContent = "";
      const title = document.createElement("div");
      title.className = "report-tip__title";
      title.textContent = data.labels[index];
      tip.appendChild(title);
      data.series.forEach((series, s) => {
        const row = document.createElement("div");
        row.className = "report-tip__row";
        const key = document.createElement("i");
        key.className = `report-key report-key--${s + 1}`;
        const value = document.createElement("strong");
        value.textContent = data.money ? money(series.values[index]) : Number(series.values[index]).toLocaleString("tr-TR");
        const name = document.createElement("span");
        name.textContent = series.name;
        row.append(key, value, name);
        tip.appendChild(row);
      });
      tip.hidden = false;
      const rect = svg.getBoundingClientRect(), boxRect = box.getBoundingClientRect();
      const px = rect.left - boxRect.left + x / data.width * rect.width;
      const onRight = px + tip.offsetWidth + 16 < box.clientWidth;
      tip.style.left = `${onRight ? px + 12 : px - tip.offsetWidth - 12}px`;
      tip.style.top = `${rect.top - boxRect.top + 12}px`;
    }
    const hide = () => { cross.hidden = true; tip.hidden = true; };

    svg.addEventListener("pointermove", (event) => {
      const rect = svg.getBoundingClientRect();
      const x = (event.clientX - rect.left) / rect.width * data.width;
      let nearest = 0;
      data.x.forEach((value, i) => { if (Math.abs(value - x) < Math.abs(data.x[nearest] - x)) nearest = i; });
      show(nearest);
    });
    svg.addEventListener("pointerleave", hide);
    svg.addEventListener("blur", hide);
    svg.addEventListener("focus", () => show(index));
    svg.addEventListener("keydown", (event) => {
      if (event.key === "ArrowLeft") { event.preventDefault(); show(index - 1); }
      if (event.key === "ArrowRight") { event.preventDefault(); show(index + 1); }
    });
  });
})();
