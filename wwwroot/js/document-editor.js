// Teklif ve sözleşme editörü. Belge içeriği DocumentJson gizli alanında JSON olarak gönderilir.
(() => {
  const form = document.getElementById("doc-form");
  if (!form) return;

  const mode = form.dataset.mode; // "quote" veya "contract"
  const clauses = JSON.parse(document.getElementById("doc-clauses").textContent);
  const designs = JSON.parse(document.getElementById("doc-designs").textContent);
  const SYSTEM = new Set(["musteri", "tarih", "toplam", "toplam_yazi", "alici", "alici_adres", "satici", "satici_adres", "konu", "madde_sayisi", "madde_sayisi_yazi", "sayfa_sayisi", "sayfa_sayisi_yazi"]);
  const content = JSON.parse(form.elements.DocumentJson.value || "{}");
  content.variables ||= {};
  content.blocks ||= [];
  content.payments ||= [];
  content.designIds ||= [];
  content.blocks.forEach((b) => { b.variables ||= {}; b.clauses ||= []; b.items ||= []; });

  const money = (v) => Number(v || 0).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " TL";

  function h(tag, attrs, ...children) {
    const el = document.createElement(tag);
    Object.entries(attrs || {}).forEach(([key, value]) => {
      if (key.startsWith("on")) el.addEventListener(key.slice(2), value);
      else if (key in el && key !== "list") el[key] = value;
      else el.setAttribute(key, value);
    });
    children.flat().filter((c) => c !== null && c !== undefined).forEach((c) => el.append(c));
    return el;
  }

  const placeholders = (text) => [...String(text || "").matchAll(/\{\{\s*([\p{L}\p{N}_]+)\s*\}\}/gu)].map((m) => m[1]).filter((n) => !SYSTEM.has(n));
  const blockPlaceholders = (b) => [...new Set(b.clauses.flatMap((c) => placeholders(c.text)).concat(placeholders(b.footer)))];
  const label = (name) => name.replaceAll("_", " ");

  function allPlaceholders() {
    const names = [content.intro, content.closing, content.note, content.subject, content.priceNote].flatMap(placeholders);
    content.blocks.forEach((b) => names.push(...blockPlaceholders(b)));
    return [...new Set(names)];
  }

  function renderVariables() {
    const box = document.getElementById("doc-variables");
    box.textContent = "";
    const names = allPlaceholders();
    if (!names.length) { box.append(h("p", { className: "admin-muted", textContent: "Metinlerde değişken yok." })); return; }
    names.forEach((name) => box.append(h("div", {},
      h("label", { textContent: label(name) }),
      h("input", { value: content.variables[name] || "", "data-variable": name, oninput: (e) => { content.variables[name] = e.target.value; refreshBlockHints(); } }))));
  }

  // Blok alanında boş bırakılan değişken, belge genelindeki değeri kullanır.
  function refreshBlockHints() {
    document.querySelectorAll("[data-block-variable]").forEach((input) => { input.placeholder = content.variables[input.dataset.blockVariable] || "Belge genelindeki değer"; });
  }

  function blockSubtotal(b) { return b.items.reduce((sum, i) => sum + (Number(i.quantity) || 0) * (Number(i.unitPrice) || 0), 0); }

  function renderTotals() {
    const box = document.getElementById("doc-total");
    if (!box || mode !== "quote") return;
    const subtotal = content.blocks.reduce((sum, b) => sum + blockSubtotal(b), 0);
    box.textContent = `Bloklar toplamı: ${money(subtotal)}`;
    document.querySelectorAll("[data-block-total]").forEach((el) => { el.textContent = money(blockSubtotal(content.blocks[Number(el.dataset.blockTotal)])); });
  }

  function renderItems(block, index) {
    const body = h("tbody");
    block.items.forEach((item, i) => {
      const update = (key) => (e) => { item[key] = e.target.type === "number" ? Number(e.target.value) : e.target.value; renderTotals(); };
      body.append(h("tr", {},
        h("td", {}, h("input", { value: item.description || "", placeholder: "Örn. Doğrama ve camlar", "aria-label": "Açıklama", oninput: update("description") })),
        h("td", {}, h("input", { type: "number", min: "0", step: "0.001", value: item.quantity ?? 1, "aria-label": "Miktar", oninput: update("quantity") })),
        h("td", {}, h("input", { value: item.unit || "Adet", "aria-label": "Birim", oninput: update("unit") })),
        h("td", {}, h("input", { type: "number", min: "0", step: "0.01", value: item.unitPrice ?? 0, "aria-label": "Tutar", oninput: update("unitPrice") })),
        h("td", {}, h("button", { type: "button", className: "doc-link", textContent: "Sil", onclick: () => { block.items.splice(i, 1); renderBlocks(); } }))));
    });
    return h("div", { className: "doc-subpanel" },
      h("h3", { textContent: "Fiyat kalemleri" }),
      h("table", { className: "doc-items" }, h("thead", {}, h("tr", {}, ["Açıklama", "Miktar", "Birim", "Birim fiyat / tutar (TL)", ""].map((t) => h("th", { textContent: t })))), body),
      h("div", { className: "doc-row" },
        h("button", { type: "button", className: "doc-btn", textContent: "+ Kalem ekle", onclick: () => { block.items.push({ description: "", quantity: 1, unit: "Adet", unitPrice: 0 }); renderBlocks(); } }),
        h("label", { className: "doc-check" }, h("input", { type: "checkbox", checked: block.showLines !== false, onchange: (e) => { block.showLines = e.target.checked; } }), " Kalemleri belgede göster"),
        h("strong", { "data-block-total": index })));
  }

  function renderClauses(block) {
    const select = h("select", { "aria-label": "Hazır madde" }, h("option", { value: "", textContent: "Hazır madde seçin" }), clauses.map((c) => h("option", { value: c.id, textContent: c.name })));
    const list = block.clauses.map((clause, i) => h("div", { className: "doc-clause" },
      h("input", { value: clause.title || "", placeholder: "Madde adı", "aria-label": "Madde adı", oninput: (e) => { clause.title = e.target.value; } }),
      h("textarea", { rows: 3, value: clause.text || "", "aria-label": "Madde metni", oninput: (e) => { clause.text = e.target.value; }, onchange: () => { renderVariables(); renderBlocks(); } }),
      h("div", { className: "doc-row" },
        h("button", { type: "button", className: "doc-link", textContent: "Yukarı", disabled: i === 0, onclick: () => { block.clauses.splice(i - 1, 0, block.clauses.splice(i, 1)[0]); renderBlocks(); } }),
        h("button", { type: "button", className: "doc-link", textContent: "Sil", onclick: () => { block.clauses.splice(i, 1); renderBlocks(); renderVariables(); } }))));
    return h("div", { className: "doc-subpanel" },
      h("h3", { textContent: "Maddeler" }), list,
      h("div", { className: "doc-row" }, select,
        h("button", { type: "button", className: "doc-btn", textContent: "Ekle", onclick: () => {
          const clause = clauses.find((c) => String(c.id) === select.value);
          block.clauses.push(clause ? { title: clause.name, text: mode === "quote" ? clause.quoteText : clause.contractText } : { title: "", text: "" });
          renderBlocks(); renderVariables();
        } })));
  }

  function renderBlocks() {
    const box = document.getElementById("doc-blocks");
    box.textContent = "";
    content.blocks.forEach((block, index) => {
      const local = blockPlaceholders(block);
      box.append(h("div", { className: "doc-block-card" },
        h("div", { className: "doc-row" },
          h("input", { value: block.name || "", placeholder: "Blok adı (örn. A Blok; tek işte boş bırakılabilir)", "aria-label": "Blok adı", className: "doc-block-title", oninput: (e) => { block.name = e.target.value; } }),
          h("button", { type: "button", className: "doc-btn", textContent: "Bloğu kopyala", onclick: () => { const copy = JSON.parse(JSON.stringify(block)); copy.name = ""; content.blocks.splice(index + 1, 0, copy); renderBlocks(); } }),
          content.blocks.length > 1 ? h("button", { type: "button", className: "doc-link", textContent: "Bloğu sil", onclick: () => { content.blocks.splice(index, 1); renderBlocks(); renderVariables(); } }) : null),
        local.length ? h("div", { className: "doc-subpanel" },
          h("h3", { textContent: "Bu bloğa özel değerler (ör. adet)" }),
          h("div", { className: "doc-grid" }, local.map((name) => h("div", {},
            h("label", { textContent: label(name) }),
            h("input", { value: block.variables[name] || "", "data-block-variable": name, oninput: (e) => { block.variables[name] = e.target.value; } }))))) : null,
        renderClauses(block),
        mode === "contract" ? h("div", { className: "doc-subpanel" },
          h("h3", { textContent: "Blok kapanış metni (isteğe bağlı)" }),
          h("textarea", { rows: 2, value: block.footer || "", "aria-label": "Blok kapanış metni", oninput: (e) => { block.footer = e.target.value; }, onchange: () => { renderBlocks(); renderVariables(); } })) : renderItems(block, index)));
    });
    refreshBlockHints();
    renderTotals();
  }

  function renderPayments() {
    const box = document.getElementById("doc-payments");
    if (!box) return;
    box.textContent = "";
    const total = () => Number(form.elements.TotalAmount.value) || 0;
    const status = h("p", { id: "doc-payment-status" });
    const updateStatus = () => {
      const sum = content.payments.reduce((s, p) => s + (Number(p.amount) || 0), 0);
      const ok = Math.abs(sum - total()) < 0.005;
      status.className = ok ? "doc-ok" : "doc-warn";
      status.textContent = ok ? `Ödeme planı toplamı bedele eşit: ${money(sum)}` : `Ödeme planı ${money(sum)}, bedel ${money(total())}. İmzadan önce eşitlenmelidir.`;
    };
    content.payments.forEach((payment, i) => {
      const needsText = payment.kind === "barter" || payment.kind === "other";
      box.append(h("div", { className: "doc-payment" },
        h("div", {}, h("label", { textContent: "Tip" }), h("select", { onchange: (e) => { payment.kind = e.target.value; renderPayments(); } },
          [["cash", "Peşin"], ["check", "Çek"], ["barter", "Barter"], ["other", "Diğer"]].map(([value, text]) => h("option", { value, textContent: text, selected: payment.kind === value })))),
        h("div", {}, h("label", { textContent: "Tutar (TL)" }), h("input", { type: "number", min: "0", step: "0.01", value: payment.amount ?? 0, oninput: (e) => { payment.amount = Number(e.target.value); updateStatus(); } })),
        payment.kind === "check" ? h("div", {}, h("label", { textContent: "Çek adedi" }), h("input", { type: "number", min: "1", value: payment.count ?? 1, oninput: (e) => { payment.count = Number(e.target.value); } })) : null,
        h("div", { className: "doc-payment-text" }, h("label", { textContent: needsText ? "Açıklama" : payment.kind === "check" ? "Çek ifadesi (boşsa \"N adet çekle\")" : "Açıklama (boşsa \"peşin olarak ödenecektir\")" }),
          h("textarea", { rows: 2, value: payment.text || "", placeholder: payment.kind === "barter" ? "Örn. {{ada}} ada, {{parsel}} parselde kayıtlı inşaatta 4 numaralı daire" : payment.kind === "check" ? "Örn. sıralı 5 ay vadeli çeklerle" : "", oninput: (e) => { payment.text = e.target.value; } })),
        h("button", { type: "button", className: "doc-link", textContent: "Sil", onclick: () => { content.payments.splice(i, 1); renderPayments(); } })));
    });
    box.append(h("button", { type: "button", className: "doc-btn", textContent: "+ Ödeme ekle", onclick: () => { content.payments.push({ kind: "cash", amount: 0, count: 1, text: "" }); renderPayments(); } }), status);
    form.elements.TotalAmount.oninput = updateStatus;
    updateStatus();
  }

  function renderDesigns() {
    const box = document.getElementById("doc-designs-list");
    box.textContent = "";
    if (!designs.length) { box.append(h("p", { className: "admin-muted", textContent: "Henüz çizim yok." })); return; }
    // Çizimler listeleriyle gruplanır: liste başlığı içindeki tüm çizimleri seçer.
    const lists = new Map();
    designs.forEach((design) => { if (!lists.has(design.listId)) lists.set(design.listId, []); lists.get(design.listId).push(design); });
    lists.forEach((items) => {
      const allChecked = items.every((design) => content.designIds.includes(design.id));
      box.append(h("label", { className: "doc-check" },
        h("input", { type: "checkbox", checked: allChecked, onchange: (e) => {
          const ids = items.map((design) => design.id);
          content.designIds = content.designIds.filter((id) => !ids.includes(id)).concat(e.target.checked ? ids : []);
          renderDesigns();
        } }),
        h("strong", { textContent: ` ${items[0].listName}` })));
      items.forEach((design) => box.append(h("label", { className: "doc-check", style: "margin-left:18px" },
        h("input", { type: "checkbox", checked: content.designIds.includes(design.id), onchange: (e) => {
          content.designIds = e.target.checked ? content.designIds.concat(design.id) : content.designIds.filter((id) => id !== design.id);
          renderDesigns();
        } }),
        ` ${design.name} (${Units.format(design.widthMm)} x ${Units.format(design.heightMm)} ${Units.code}, ${design.quantity} adet)`)));
    });
  }

  // Sözleşme alanları, müşteri seçilince adres ve vergi bilgisiyle dolar; yazılanın üstüne yazmaz.
  const customers = JSON.parse(document.getElementById("doc-customers")?.textContent || "[]");
  if (mode === "contract") form.elements.CustomerId.addEventListener("change", (e) => {
    const customer = customers.find((c) => String(c.id) === e.target.value);
    if (!customer) return;
    if (!form.elements.BuyerName.value) form.elements.BuyerName.value = customer.name || "";
    if (!form.elements.BuyerAddress.value) form.elements.BuyerAddress.value = customer.address || "";
    if (!form.elements.BuyerTaxInfo.value) form.elements.BuyerTaxInfo.value = customer.taxInfo || "";
  });

  document.querySelectorAll("[data-content]").forEach((input) => {
    const key = input.dataset.content;
    input.value = content[key] || "";
    input.addEventListener("input", () => { content[key] = input.value; });
    input.addEventListener("change", renderVariables);
  });
  document.getElementById("doc-add-block").addEventListener("click", () => {
    content.blocks.push({ name: "", showLines: true, variables: {}, clauses: [], items: mode === "quote" ? [{ description: "", quantity: 1, unit: "Adet", unitPrice: 0 }] : [] });
    renderBlocks();
  });
  form.addEventListener("submit", () => { form.elements.DocumentJson.value = JSON.stringify(content); });

  renderVariables();
  renderBlocks();
  renderPayments();
  renderDesigns();
})();
