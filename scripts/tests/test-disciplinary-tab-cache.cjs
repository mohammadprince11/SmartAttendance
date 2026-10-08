// Offline synthetic GETs only. No application server, accounts, employee files or database.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require('playwright');
const source = fs.readFileSync(path.join(__dirname, '../../SmartAttendance.Web/wwwroot/js/zynora-disciplinary-workspace.js'), 'utf8');
assert(!source.includes('localStorage') && !source.includes('sessionStorage'));
const html = (tab, company = '1') => `<html dir="rtl"><head></head><body><aside id="shell">shell</aside>
<section data-disciplinary-workspace><p data-disciplinary-status hidden role="status"></p><nav>
${['library','articles','templates','designer'].map(key => `<a class="zyw-tab" href="/DisciplinaryRules?tab=${key}&CompanyId=${company}" aria-selected="${key === tab}">${key}</a>`).join('')}
</nav><main><p id="company">${company}</p><form method="post"><input name="__RequestVerificationToken" value="synthetic-token"><input id="edit" name="name" value="default-${tab}"><button>save</button></form></main></section></body></html>`;
(async () => {
    const browser = await chromium.launch({channel:'msedge', headless:true});
    let checks = 1;
    try {
        for (const width of [390, 1440]) {
            const page = await browser.newPage({viewport:{width,height:850}});
            let gets = 0, slow = false, fail = false;
            await page.route('https://disciplinary.test/**', async route => {
                assert.equal(route.request().method(), 'GET');
                gets++;
                if (fail) { fail = false; return route.fulfill({status:403,body:'denied'}); }
                if (slow) await new Promise(resolve => setTimeout(resolve, 450));
                const url = new URL(route.request().url());
                return route.fulfill({contentType:'text/html',body:html(url.searchParams.get('tab') || 'library', url.searchParams.get('CompanyId') || '1')});
            });
            await page.goto('https://disciplinary.test/DisciplinaryRules?CompanyId=1');
            await page.evaluate(() => {
                window.timeOffset = 0; const now = Date.now;
                Date.now = () => now() + window.timeOffset;
                document.querySelector('#shell').sentinel = 42;
            });
            await page.addScriptTag({content:source});
            const status = () => page.locator('[data-disciplinary-status]');
            async function choose(tab) {
                await page.click(`.zyw-tab[href*="tab=${tab}"]`);
                await page.waitForFunction(tab => document.querySelector('[aria-selected=true]').textContent === tab && !document.querySelector('[aria-busy=true]'), tab);
            }
            await choose('templates'); const afterFirst = gets;
            assert.equal(afterFirst, 2); checks++;
            await choose('library');
            assert.equal(gets, afterFirst, 'initial GET reused with canonical default tab'); checks++;
            await choose('templates');
            assert.equal(gets, afterFirst, 'visited tab needs no section request'); checks++;
            assert(await status().isHidden(), 'no waiting flash on cached navigation'); checks++;
            assert.equal(await page.evaluate(() => document.querySelector('#shell').sentinel), 42); checks++;
            await page.locator('#edit').fill('unsaved');
            page.once('dialog', dialog => dialog.dismiss());
            await page.click('.zyw-tab[href*="tab=library"]');
            assert.equal(await page.locator('#edit').inputValue(), 'unsaved'); checks++;
            assert.equal(gets, afterFirst); checks++;
            page.once('dialog', dialog => dialog.accept()); await choose('library'); await choose('templates');
            assert.equal(await page.locator('#edit').inputValue(), 'default-templates', 'discard does not contaminate raw GET snapshot'); checks++;
            assert.equal(await page.locator('[name=__RequestVerificationToken]').inputValue(), 'synthetic-token'); checks++;
            // No real submit. Capture-phase invalidation must still happen on a prevented POST.
            await page.evaluate(() => {
                document.addEventListener('submit', event => event.preventDefault(), {once:true});
                document.querySelector('form').dispatchEvent(new Event('submit', {bubbles:true,cancelable:true}));
            });
            await choose('library'); assert.equal(gets, afterFirst + 1, 'POST invalidates previous snapshots'); checks++;
            await choose('templates'); const beforeExpiry = gets;
            await page.evaluate(() => { window.timeOffset += 31000; });
            await choose('library'); assert.equal(gets, beforeExpiry + 1, 'expired section requests fresh authorization/data'); checks++;
            await choose('templates'); const beforeHide = gets;
            await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
            await choose('library'); assert.equal(gets, beforeHide + 1); checks++;
            await page.evaluate(() => {
                Object.defineProperty(document, 'hidden', {configurable:true,value:true});
                document.dispatchEvent(new Event('visibilitychange'));
                Object.defineProperty(document, 'hidden', {configurable:true,value:false});
            });
            await choose('templates'); assert.equal(gets, beforeHide + 2, 'hidden document drops snapshots'); checks++;
            slow = true;
            await page.click('.zyw-tab[href*="tab=articles"]');
            assert(await status().isHidden(), 'no immediate loading text'); checks++;
            await page.waitForFunction(() => document.querySelector('[data-disciplinary-status]').textContent.includes('جاري'));
            checks++;
            await page.waitForFunction(() => document.querySelector('[aria-selected=true]').textContent === 'articles' && !document.querySelector('[aria-busy=true]'));
            assert(await status().isHidden(), 'loading text clears on completion'); checks++;
            slow = false;
            await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
            fail = true;
            await page.click('.zyw-tab[href*="tab=designer"]');
            await page.waitForFunction(() => document.querySelector('[data-disciplinary-status]').textContent.includes('تعذّر'));
            assert.equal(await page.locator('[aria-selected=true]').textContent(), 'articles'); checks++;
            assert(!await page.locator('main').evaluate(main => main.inert)); checks++;
            await choose('templates'); const beforeScope = gets;
            await page.evaluate(() => { document.querySelector('.zyw-tab[href*="tab=library"]').href = '/DisciplinaryRules?CompanyId=2&tab=library'; });
            await choose('library');
            assert.equal(gets, beforeScope + 1); checks++;
            assert.equal(await page.locator('#company').textContent(), '2', 'full scope/query key remains separate'); checks++;
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true); checks++;
            await page.close();
        }
        console.log(`Disciplinary tab cache: ${checks} checks passed (offline, two widths).`);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
