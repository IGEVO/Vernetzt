const preference = localStorage.getItem('themePreference') || 'System'
const dark = preference === 'Dark' || (preference === 'System' && matchMedia('(prefers-color-scheme: dark)').matches)
document.documentElement.dataset.theme = preference
document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
document.documentElement.style.backgroundColor = dark ? '#121212' : '#F5F5F5'
