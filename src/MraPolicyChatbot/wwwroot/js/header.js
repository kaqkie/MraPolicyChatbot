(function(){
  const btn = document.getElementById('profileButton');
  const dropdown = document.getElementById('profileDropdown');
  if (!btn || !dropdown) return;

  function openDropdown(){
    dropdown.hidden = false;
    btn.setAttribute('aria-expanded','true');
    // focus first focusable element inside dropdown
    const focusable = dropdown.querySelector('button, a, input');
    if (focusable) focusable.focus();
  }

  function closeDropdown(){
    dropdown.hidden = true;
    btn.setAttribute('aria-expanded','false');
  }

  btn.addEventListener('click', function(e){
    e.preventDefault();
    if (dropdown.hidden) openDropdown(); else closeDropdown();
  });

  document.addEventListener('click', function(e){
    if (!dropdown.hidden && !dropdown.contains(e.target) && e.target !== btn && !btn.contains(e.target)){
      closeDropdown();
    }
  });

  document.addEventListener('keydown', function(e){
    if (e.key === 'Escape' && !dropdown.hidden){
      closeDropdown();
      btn.focus();
    }
  });
})();
