# The landing platform's deck, shared by build_pad.py (its UVs) and pad_maps.py
# (the maps on them). Metres of the model, from above.

DECK_HALF = 5.1         # half the deck's width
JOINT_DEPTH = 0.06      # how deep the joints look (the small pad doubles the model: 12 cm)
JOINT_SOFT = 0.028      # how soft their edges are
PLATE_HALF = 2.31       # half the central plate's width
DIMPLE_RADIUS = 0.085

# One corner's six dimples; the other corners have them turned by quarter turns
CORNER = [(1.644, 2.152), (2.119, 2.153), (1.407, 1.983), (1.881, 1.987), (1.644, 1.818), (2.118, 1.821)]


def dimples():
    at = []
    for x, y in CORNER:
        at += [(x, y), (-y, x), (-x, -y), (y, -x)]
    return at
