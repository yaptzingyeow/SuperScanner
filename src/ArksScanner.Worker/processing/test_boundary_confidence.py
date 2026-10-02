import unittest

from boundary.confidence import score_geometry
from boundary.geometry import GeometryEvidence


class BoundaryConfidenceTests(unittest.TestCase):
    def test_scores_strong_geometry_as_high_confidence(self):
        evidence = GeometryEvidence(.95, .68, .99, (.94, .92, .96, .93), .98, .96)
        self.assertGreaterEqual(score_geometry(evidence), .78)

    def test_weakest_side_limits_confidence(self):
        strong = GeometryEvidence(.9, .7, .95, (.9, .9, .9, .9), .96, .94)
        weak_side = GeometryEvidence(.9, .7, .95, (.9, .9, .12, .9), .96, .94)
        self.assertGreater(score_geometry(strong), score_geometry(weak_side))

    def test_score_is_clamped_to_unit_interval(self):
        evidence = GeometryEvidence(4, .7, 4, (4, 4, 4, 4), 4, 4)
        self.assertEqual(1.0, score_geometry(evidence))


if __name__ == "__main__":
    unittest.main()
