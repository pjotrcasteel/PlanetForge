import { expect, test } from '@playwright/test';

test.setTimeout(240_000);
const url = '/?visualTest=1&seed=24061984';

async function materialStatistics(page) {
  return page.evaluate(() => {
    window.__planetForgeSurfaceTest.measure();
    const canvas = document.querySelector('#planet-canvas');
    const gl = canvas.getContext('webgl2');
    const pixels = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let sum=0, count=0, min=1, max=0, ocean=0;
    for(let y=Math.floor(canvas.height*.3);y<canvas.height*.7;y+=3) {
      for(let x=Math.floor(canvas.width*.4);x<canvas.width*.6;x+=3) {
        const i=(y*canvas.width+x)*4;
        const r=pixels[i]/255,g=pixels[i+1]/255,b=pixels[i+2]/255;
        const value=.2126*r+.7152*g+.0722*b;
        sum+=value;count++;min=Math.min(min,value);max=Math.max(max,value);
        if(b>r*1.35 && g>r*1.3)ocean++;
      }
    }
    return { mean:sum/count, range:max-min, oceanFraction:ocean/count, glError:gl.getError() };
  });
}

test('Formation_DefaultStartsMolten_UsesMinimalUIAndPhysicalOptics', async ({ page }, testInfo) => {
  await page.setViewportSize({width:1440,height:900});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(url);
  await expect(page.getByTestId('formation-phase')).toHaveText('Molten surface',{timeout:60000});
  await expect(page.getByTestId('formation-time')).toHaveText('T+00:00:00');
  await expect(page.getByTestId('airless-enter')).toHaveCount(0);
  await expect(page.getByTestId('regional-switch')).toHaveCount(0);
  await expect(page.getByRole('button',{name:'NEXT',exact:true})).toHaveCount(0);
  await page.waitForFunction(()=>Boolean(window.__planetForgeSurfaceTest),null,{timeout:60000});
  const stats=await materialStatistics(page);
  expect(stats.glError).toBe(0);expect(stats.mean).toBeGreaterThan(.12);
  expect(stats.range).toBeGreaterThan(.025);expect(stats.oceanFraction).toBeLessThan(.01);
  await expect(page.getByTestId('formation-play')).toBeInViewport();
  await page.screenshot({path:testInfo.outputPath('formation-molten-pc.png')});
  expect(errors).toEqual([]);
});

test('Formation_OneClock_CoolsAndReplaysWithoutStateSwitchButtons', async ({ page }, testInfo) => {
  await page.setViewportSize({width:1440,height:900});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(url);
  await expect(page.getByTestId('formation-phase')).toHaveText('Molten surface',{timeout:60000});
  await page.waitForFunction(()=>Boolean(window.__planetForgeSurfaceTest),null,{timeout:60000});
  const initial=await materialStatistics(page);
  await page.getByTestId('formation-speed').selectOption('30');
  await page.getByTestId('formation-step').click();
  await expect(page.getByTestId('formation-time')).toHaveText('T+00:00:30',{timeout:90000});
  await expect(page.getByTestId('formation-step')).toBeEnabled({timeout:60000});
  await expect(page.getByTestId('formation-phase')).toHaveText('Surface crystallizing');
  const cooled=await materialStatistics(page);
  expect(cooled.mean).toBeLessThan(initial.mean*.8);expect(cooled.glError).toBe(0);
  await page.locator('.formation-details summary').click();
  await expect(page.getByTestId('formation-melt')).not.toHaveText('TOP MELT 100%–100%');
  await page.screenshot({path:testInfo.outputPath('formation-crystallizing-pc.png')});
  await page.getByTestId('formation-speed').selectOption('1');
  await page.getByTestId('formation-play').click();
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed','true');
  await expect(page.getByTestId('formation-time')).not.toHaveText('T+00:00:30',{timeout:60000});
  await page.getByTestId('formation-play').click();
  await expect(page.getByTestId('formation-step')).toBeEnabled({timeout:60000});
  const paused=await page.getByTestId('formation-time').innerText();
  await page.waitForTimeout(500);await expect(page.getByTestId('formation-time')).toHaveText(paused);
  await page.getByTestId('formation-restart').click();
  await expect(page.getByTestId('formation-time')).toHaveText('T+00:00:00',{timeout:60000});
  await expect(page.getByTestId('formation-phase')).toHaveText('Molten surface');
  expect(errors).toEqual([]);
});

test('Formation_OrbitAndLocalLOD_KeepThermalMaterialWithoutWebGLErrors', async ({ page }, testInfo) => {
  await page.goto(url);
  await expect(page.getByTestId('formation-phase')).toHaveText('Molten surface',{timeout:60000});
  await page.waitForFunction(()=>Boolean(window.__planetForgeSurfaceTest),null,{timeout:60000});
  await page.evaluate(()=>window.__planetForgeSurfaceTest.setDistance(1.003));
  await expect.poll(()=>page.evaluate(()=>window.__planetForgeSurfaceTest.getLodStats().mode),{timeout:90000}).toBe('local');
  expect((await page.evaluate(()=>window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
  await page.screenshot({path:testInfo.outputPath('formation-molten-local.png')});
  await page.evaluate(()=>window.__planetForgeSurfaceTest.setDistance(3.5));
  await expect.poll(()=>page.evaluate(()=>window.__planetForgeSurfaceTest.getLodStats().mode),{timeout:60000}).toBe('globe');
  await expect(page.getByTestId('formation-time')).toHaveText('T+00:00:00');
  expect((await page.evaluate(()=>window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
});

test('Formation_Portrait_ControlsFitAndAdvanceWithoutChangingScenario', async ({ page }) => {
  await page.setViewportSize({width:390,height:844});
  await page.goto(url);
  await expect(page.getByTestId('formation-phase')).toHaveText('Molten surface',{timeout:60000});
  for(const id of ['formation-play','formation-step','formation-speed','formation-restart']) await expect(page.getByTestId(id)).toBeInViewport();
  await page.getByTestId('formation-step').click();
  await expect(page.getByTestId('formation-time')).toHaveText('T+00:00:01',{timeout:60000});
  expect((await page.evaluate(()=>window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
});
