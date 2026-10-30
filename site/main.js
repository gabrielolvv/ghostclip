'use strict';

// Fills in the latest version, the installer size and the star count from GitHub.
// The page works without it: the download link always points at the newest release.
const repo = 'gabrielolvv/ghostclip';

function mb(bytes) { return `${Math.round(bytes / 1024 / 1024)} MB`; }

fetch(`https://api.github.com/repos/${repo}/releases/latest`)
  .then(r => (r.ok ? r.json() : Promise.reject(r.status)))
  .then(release => {
    const setup = (release.assets || []).find(a => a.name === 'Ghostclip-win-Setup.exe');
    const parts = [`Version ${release.tag_name.replace(/^v/, '')}`];
    if (setup) parts.push(mb(setup.size));
    parts.push('Windows 10 2004+', 'NVIDIA, AMD or Intel GPU');
    document.getElementById('release').textContent = parts.join(' · ');
  })
  .catch(() => {});

fetch(`https://api.github.com/repos/${repo}`)
  .then(r => (r.ok ? r.json() : Promise.reject(r.status)))
  .then(info => {
    if (info.stargazers_count > 0) {
      document.getElementById('stars').textContent = info.stargazers_count.toLocaleString('en-US');
    }
  })
  .catch(() => {});

// Not on Windows (a phone, a Mac): say so instead of offering a file that won't run there.
const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
if (platform && !/win/i.test(platform)) {
  document.getElementById('not-windows').hidden = false;
}
