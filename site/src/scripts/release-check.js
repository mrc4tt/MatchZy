// Release check in the browser: the site is built from the newest git tag at deploy time, so a
// release made after the last deploy would not show until the next one. This asks GitHub for the
// newest release and, when it is newer than the build, updates every element marked with:
//   data-mz-version         text becomes the new version
//   data-mz-download        href becomes the new zip
//   data-mz-version-text    occurrences of the built version in its text are replaced
//   data-mz-unreleased="x"  removed once version x is released ("Not released yet" marks)
// Read-only, no credentials; GitHub's limit is 60 unauthenticated calls per hour per address, so
// the answer is kept in sessionStorage for an hour. Any failure leaves the built page as it is.
const REPO = 'mrc4tt/MatchZy';
const KEY = 'mz-latest-release';
const MAX_AGE = 60 * 60 * 1000;

const parse = (v) => {
	const m = /^v?(\d+)\.(\d+)\.(\d+)$/.exec(String(v || '').trim());
	return m ? m.slice(1).map(Number) : null;
};
const newer = (a, b) => {
	const x = parse(a), y = parse(b);
	if (!x || !y) return false;
	return x[0] - y[0] > 0 || (x[0] === y[0] && (x[1] - y[1] > 0 || (x[1] === y[1] && x[2] > y[2])));
};
const atLeast = (a, b) => !newer(b, a) && parse(a) && parse(b);

function cached() {
	try {
		const c = JSON.parse(sessionStorage.getItem(KEY) || 'null');
		return c && Date.now() - c.at < MAX_AGE ? c.tag : null;
	} catch {
		return null;
	}
}

async function latestTag() {
	const hit = cached();
	if (hit) return hit;
	const res = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { headers: { Accept: 'application/vnd.github+json' } });
	if (!res.ok) return null;
	const tag = String((await res.json()).tag_name || '').replace(/^v/, '');
	if (!parse(tag)) return null;
	try {
		sessionStorage.setItem(KEY, JSON.stringify({ tag, at: Date.now() }));
	} catch {
		// Storage blocked: just ask again next time.
	}
	return tag;
}

// The version the page was built with: data-mz-built on any element, else nothing to compare.
export async function checkRelease() {
	const built = document.querySelector('[data-mz-built]')?.getAttribute('data-mz-built');
	if (!built) return;
	const marked = document.querySelector('[data-mz-version], [data-mz-download], [data-mz-version-text], [data-mz-unreleased]');
	if (!marked) return;
	let tag;
	try {
		tag = await latestTag();
	} catch {
		return;
	}
	if (!tag) return;
	for (const el of document.querySelectorAll('[data-mz-unreleased]')) {
		if (atLeast(tag, el.getAttribute('data-mz-unreleased'))) el.remove();
	}
	if (!newer(tag, built)) return;
	for (const el of document.querySelectorAll('[data-mz-version]')) el.textContent = tag;
	for (const el of document.querySelectorAll('[data-mz-download]')) el.setAttribute('href', `https://github.com/${REPO}/releases/download/${tag}/MatchZy-${tag}.zip`);
	for (const el of document.querySelectorAll('[data-mz-version-text]')) {
		const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
		for (let n = walker.nextNode(); n; n = walker.nextNode()) n.nodeValue = n.nodeValue.split(built).join(tag);
	}
}
