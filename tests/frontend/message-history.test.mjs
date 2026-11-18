import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const source = await readFile(new URL('../../frontend/wwwroot/js/message-history.js', import.meta.url), 'utf8');
const {bind, unbind} = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
globalThis.requestAnimationFrame = callback => callback();
function element() {
 return {scrollTop: 0, scrollHeight: 1000, addEventListener(_, cb) {this.listener=cb;}, removeEventListener(_, cb) {if(this.listener===cb)this.listener=null;}};
}
test('scrolling to top loads once and preserves viewport after prepend', async () => {
 const el=element(); let calls=0, release;
 const wait=new Promise(resolve=>release=resolve);
 bind(el,{async invokeMethodAsync(method){assert.equal(method,'LoadOlderFromScroll');calls++;await wait;el.scrollHeight=1400;}},'a',true,10);
 el.scrollTop=0;
 const first=el.listener(); await el.listener(); assert.equal(calls,1);
 release(); await first; assert.equal(el.scrollTop,400);
 unbind(el); assert.equal(el.listener,null);
});
test('disabled history does not load and conversation switch resets position', async () => {
 const el=element(); let calls=0;
 const receiver={async invokeMethodAsync(){calls++;}};
 bind(el,receiver,'a',false,10);el.scrollTop=0;await el.listener();assert.equal(calls,0);
 bind(el,receiver,'b',true,10);assert.equal(el.scrollTop,1000);
 unbind(el);
});
