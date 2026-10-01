document.addEventListener('DOMContentLoaded', function () {
  // Table search/filter
  const search = document.getElementById('policySearch');
  const filter = document.getElementById('statusFilter');
  const table = document.getElementById('policiesTable');
  const empty = document.getElementById('emptyState');

  function applyFilter() {
    if (!table) return;
    const q = search ? search.value.trim().toLowerCase() : '';
    const s = filter ? filter.value : '';
    let visible = 0;
    table.querySelectorAll('tbody tr').forEach(tr => {
      const rowText = tr.textContent.trim().toLowerCase();
      const status = (tr.getAttribute('data-status') || '').toLowerCase();
      const matchesQ = !q || rowText.includes(q);
      const matchesS = !s || status === s.toLowerCase();
      if (matchesQ && matchesS) { tr.style.display=''; visible++; } else { tr.style.display='none'; }
    });
    if (empty) empty.style.display = visible ? 'none' : '';
  }

  if (search) search.addEventListener('input', applyFilter);
  if (filter) filter.addEventListener('change', applyFilter);

  // File drop zone
  const drop = document.getElementById('fileDrop');
  const fileInput = document.getElementById('policyFileInput');
  const chosen = document.getElementById('chosenFile');
  if (drop && fileInput) {
    drop.addEventListener('click', () => fileInput.click());
    drop.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') fileInput.click(); });
    ['dragenter','dragover'].forEach(ev => drop.addEventListener(ev, (e)=>{ e.preventDefault(); drop.classList.add('dragover'); }));
    ['dragleave','drop'].forEach(ev => drop.addEventListener(ev, (e)=>{ e.preventDefault(); drop.classList.remove('dragover'); }));
    drop.addEventListener('drop', (e)=>{ const f = e.dataTransfer.files[0]; if (f) { fileInput.files = e.dataTransfer.files; chosen.textContent = f.name; applyFilter(); } });
    fileInput.addEventListener('change', ()=>{ const f = fileInput.files[0]; if (f) chosen.textContent = f.name; else chosen.textContent = ''; applyFilter(); });
  }

  // Small toggles accessible niceties (for settings page)
  document.querySelectorAll('.form-check-input').forEach(chk => {
    chk.addEventListener('keydown', (e)=>{ if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); chk.checked = !chk.checked; chk.dispatchEvent(new Event('change')); } });
  });

  // Initial filter pass in case of pre-filled values
  applyFilter();
});
