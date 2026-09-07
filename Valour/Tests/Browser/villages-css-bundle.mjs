import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, rm, mkdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { execFileSync } from 'node:child_process';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const temporary=await mkdtemp(resolve(tmpdir(),'valour-css-query-'));
const output=resolve('TestResults/village-browser/css-bundle');await mkdir(output,{recursive:true});
const checks=[];
const record=name=>{checks.push(name);console.log(`PASS ${name}`);};
const browser=await chromium.launch({headless:true,executablePath:process.env.BROWSER_EXECUTABLE});
try {
 const source=`
 .world { container-type: size; container-name: village; width: 667px; height: 331px; }
 #combined, #nested { color: rgb(255,0,0); }
 @container village (min-width: 520px) and (max-height: 500px) { #combined { color: rgb(0,255,0); } }
 @container village (min-width: 520px) { @container village (max-height: 500px) { #nested { color: rgb(0,255,0); } } }
 #quoted::after { content: "@container fake { keep this literal }"; }
 @container village style(--label: "quoted { brace }") { #other { color: blue; } }
 /* @container ignored { comment } */
 `;
 await writeFile(`${temporary}/input.css`,source);await writeFile(`${temporary}/scoped.css`,'');
 execFileSync('dotnet',['run','--no-build','--project',resolve(import.meta.dirname,'../../BuildTools/CssBundler/CssBundler.csproj'),'--',`${temporary}/input.css`,`${temporary}/scoped.css`,temporary,`${temporary}/output.css`],{stdio:'pipe'});
 const css=await readFile(`${temporary}/output.css`,'utf8');
 assert.ok(css.includes('style(--label: "quoted { brace }")'), 'Quoted braces inside a condition survive minification');
 assert.ok(!css.includes('valour_query_'),'Internal placeholders never reach the bundle');
 const page=await browser.newPage();
 await page.setContent(`<style>${css}</style><div class="world"><div id="combined"></div><div id="nested"></div><div id="quoted"></div></div>`);
 assert.deepEqual(await page.evaluate(()=>['combined','nested'].map(id=>getComputedStyle(document.getElementById(id)).color)),['rgb(0, 255, 0)','rgb(0, 255, 0)']);
 record('Combined and nested container conditions remain active after release minification');
 assert.equal(await page.evaluate(()=>getComputedStyle(document.getElementById('quoted'),'::after').content),'"@container fake { keep this literal }"');
 record('CSS strings and quoted query values survive without replacement tokens');
 await page.locator('.world').evaluate(el=>el.style.width='400px');
 assert.deepEqual(await page.evaluate(()=>['combined','nested'].map(id=>getComputedStyle(document.getElementById(id)).color)),['rgb(255, 0, 0)','rgb(255, 0, 0)']);
 record('Conditions stop applying when the pane moves below the width breakpoint');
 execFileSync('dotnet',['run','--no-build','--project',resolve(import.meta.dirname,'../../BuildTools/CssBundler/CssBundler.csproj'),'--',`${temporary}/input.css`,`${temporary}/scoped.css`,temporary,`${temporary}/again.css`],{stdio:'pipe'});
 assert.equal(await readFile(`${temporary}/again.css`,'utf8'),css);
 record('Repeated bundling produces identical CSS without random markers');
} finally { await writeFile(`${output}/results.json`,JSON.stringify({checks},null,2));await browser.close();await rm(temporary,{recursive:true,force:true}); }
