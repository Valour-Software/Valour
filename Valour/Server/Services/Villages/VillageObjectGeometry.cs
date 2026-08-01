namespace Valour.Server.Services.Villages;

/// <summary>
/// Ground footprint used to anchor village object art and collision. Sprite
/// height is intentionally not used here: trees are drawn upward from a small
/// footprint rather than occupying every tile covered by their canopy.
/// </summary>
internal static class VillageObjectGeometry
{
    public static (int Width, int Height) GetFootprint(string definitionKey) =>
        definitionKey switch
        {
            "small-tree" or
            "small-tree.with-grass" or
            "small-tree-planter" or
            "small-tree-planter.square" => (2, 1),
            "trees.large-tree" or
            "trees.large-tree.with-grass" or
            "trees.large-tree-planter" or
            "large-tree-planter.square" => (3, 1),
            "furniture.park-bench" => (2, 1),
            "decor.stone-fountain" => (2, 2),
            "garden.flowers.white" or
            "garden.flowers.pink" or
            "garden.flowers.red" or
            "garden.planter.white" or
            "garden.planter.yellow" or
            "garden.planter.pink" => (2, 1),
            "commerce.market-stall" => (2, 1),
            "buildings.house-medium" or
            "buildings.house-medium.brown" or
            "buildings.house-medium.blue" => (8, 5),
            "buildings.police-station.blue" => (7, 5),
            "buildings.apartment-medium-brown" => (6, 4),
            "buildings.apartment-small-brown" => (6, 4),
            "buildings.apartment-tall-brown" => (7, 5),
            "buildings.apartment-medium-grey" => (6, 5),
            _ => (1, 1),
        };
}
