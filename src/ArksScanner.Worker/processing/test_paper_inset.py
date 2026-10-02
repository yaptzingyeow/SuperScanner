import unittest
import numpy as np
from boundary.refine_edges import inset_paper_edges


class PaperInsetTests(unittest.TestCase):
    def test_parallel_edges_move_inside_by_two_pixels_without_large_margin_loss(self):
        points = np.array([[.1, .1], [.9, .1], [.9, .9], [.1, .9]])
        actual = inset_paper_edges(points, 1001, 1001)
        np.testing.assert_allclose(actual, [[.102, .102], [.898, .102],
                                          [.898, .898], [.102, .898]], atol=1e-6)

    def test_slanted_edges_stay_parallel_and_all_corners_inside(self):
        points = np.array([[.2, .1], [.85, .17], [.95, .9], [.1, .85]])
        actual = inset_paper_edges(points, 1001, 1001)
        for index in range(4):
            edge = points[(index+1) % 4] - points[index]
            replacement = actual[(index+1) % 4] - actual[index]
            self.assertAlmostEqual(float(edge[0]*replacement[1]-edge[1]*replacement[0]), 0, places=6)
            for corner in actual:
                delta = corner-points[index]
                self.assertGreater(float(edge[0]*delta[1]-edge[1]*delta[0]), 0)

if __name__ == '__main__':
    unittest.main()
