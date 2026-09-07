import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// Razor's collocated module is served at a different relative root than its source.
const path = new URL('../../Client/Components/Windows/Villages/VillageWindowComponent.razor.js', import.meta.url);
const source = (await readFile(path, 'utf8')).replaceAll('"../../../ts/', `"${new URL('../../Client/wwwroot/ts/', import.meta.url).href}`);
const { isValidBuildPlacement, restoreOptimisticTerrain, getDecorationSpriteBounds, getDecorationDepth, isHandledPointerClick } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const rug = {id: 'rug', x: 3, y: 3, width: 4, height: 3, zIndex: -10};
const desk = {id: 'desk', definitionKey: 'desk', x: 4, y: 4, width: 2, height: 2, zIndex: 0};
function setup(definition = {}, tool = 'Furnish') {
    const map = {id: 'map', width: 18, height: 13, canEdit: true, spawnTile: {x:9,y:11}, decorations: [desk], groundTiles: [rug]};
    const state = {build:{tool, definition}, scene:{buildCatalog:[{key:'desk',supportsItems:true}], maps:[map]}};
    return {map, state};
}
test('full furniture footprints are checked against boundaries, entrances and furniture', () => {
    const {map,state}=setup();
    assert.equal(isValidBuildPlacement(state,map,17,2,3,1),false);
    assert.equal(isValidBuildPlacement(state,map,8,10,3,2),false);
    assert.equal(isValidBuildPlacement(state,map,3,4,3,1),false);
    assert.equal(isValidBuildPlacement(state,map,3,3,3,1),true);
});
test('rugs fit beneath furniture but cannot overlap another rug', () => {
    const {map,state}=setup({placementLayer:'Floor'});
    map.groundTiles=[];
    assert.equal(isValidBuildPlacement(state,map,3,3,4,3),true);
    map.groundTiles=[rug];
    assert.equal(isValidBuildPlacement(state,map,3,3,4,3),false);
});
test('desk accessories need a containing desk and cannot overlap each other', () => {
    const {map,state}=setup({placementLayer:'Surface'});
    assert.equal(isValidBuildPlacement(state,map,4,4,1,1),true);
    assert.equal(isValidBuildPlacement(state,map,1,1,1,1),false);
    assert.equal(isValidBuildPlacement(state,map,5,4,2,1),false);
    map.decorations.push({id:'monitor',x:4,y:4,width:1,height:1,zIndex:5});
    assert.equal(isValidBuildPlacement(state,map,4,4,1,1),false);
});
test('moving furniture ignores its old footprint and respects the target property', () => {
    const {map,state}=setup({},'Move');state.build.movingObject=desk;
    assert.equal(isValidBuildPlacement(state,map,4,4,2,2),true);
    map.canEdit=false;map.plots=[{x:1,y:1,width:6,height:6,canEdit:true}];
    assert.equal(isValidBuildPlacement(state,map,7,4,2,2),false);
});
test('a rejected paint stroke restores rugs and invalidates optimistic collision', () => {
    const {map,state}=setup();state.collisionByMap=new Map([['map',{}]]);
    state.build.optimisticRollback={mapId:'map',groundTiles:[rug]};map.groundTiles=[];
    restoreOptimisticTerrain(state);
    assert.deepEqual(map.groundTiles,[rug]);assert.equal(state.collisionByMap.has('map'),false);
});
test('surface art is lifted onto its support and sorted above the entire desk', () => {
    const item = {x:4,y:4,width:2,height:1,zIndex:5};
    const bounds = getDecorationSpriteBounds(item, {tilesWide:2,tilesHigh:2});
    assert.deepEqual(bounds, {x:4,y:2.5,width:2,height:2});
    assert.equal(getDecorationDepth(item, {decorations:[desk]}), 6.01);
    assert.equal(getDecorationDepth(desk, {decorations:[desk,item]}), 6);
});

test('native clicks are deduplicated when browsers round pointer coordinates', () => {
    const handled = {x: 546.5, y: 421.5, at: 100};
    assert.equal(isHandledPointerClick({type:'click',clientX:546,clientY:421},handled,105),true);
    assert.equal(isHandledPointerClick({type:'pointerup',clientX:546.5,clientY:421.5},handled,110),false);
    assert.equal(isHandledPointerClick({type:'click',clientX:595,clientY:421},handled,110),false);
    assert.equal(isHandledPointerClick({type:'click',clientX:546,clientY:421},handled,1000),false);
});


test('furniture is centered on its footprint and surface items stay inside the tabletop', () => {
    assert.deepEqual(getDecorationSpriteBounds({x:5,y:6,width:1,height:1,zIndex:0}, {tilesWide:2,tilesHigh:2}),
        {x:4.5,y:5,width:2,height:2});
    const map={decorations:[{x:4,y:4,width:3,height:2,zIndex:0}]};
    assert.equal(getDecorationSpriteBounds({x:4,y:4,width:2,height:1,zIndex:5}, {tilesWide:2,tilesHigh:2}, map).x, 4.25);
    assert.equal(getDecorationSpriteBounds({x:5,y:4,width:2,height:1,zIndex:5}, {tilesWide:2,tilesHigh:2}, map).x, 4.75);
});
