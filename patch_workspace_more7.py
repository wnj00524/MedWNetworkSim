import re

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'r') as f:
    content = f.read()

search_block = """                UpdateSceneNodeLayout(node, nodeModel, pressure.Score > 0d ? pressure : null, graphRenderer.GetZoomTier(Viewport.Zoom));
                node.HasWarning = pressure.Score > 0d || (state != null && state.DemandBacklog > 0d);
            }
        }"""

replace_block = """                UpdateSceneNodeLayout(node, nodeModel, pressure.Score > 0d ? pressure : null, graphRenderer.GetZoomTier(Viewport.Zoom));
                node.HasWarning = pressure.Score > 0d || state.DemandBacklog > 0d;
            }
        }"""

content = content.replace(search_block, replace_block)

with open('src/MedWNetworkSim.Presentation/WorkspacePresentation.cs', 'w') as f:
    f.write(content)

print("Patch 7 applied.")
