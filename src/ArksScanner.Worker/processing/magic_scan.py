"""Automatic document scan: boundary detection, true-aspect flattening, clean-up.

Detection scores candidate quadrilaterals by boundary evidence (edge support
along each side and a colour step across it), not by area, so a small card on
a textured desk and a printed frame inside a sheet are handled by the same
rule. Enhancement never generates content: it normalises illumination, maps
paper to white and ink to dark, and removes only debris at the page border.
"""
import cv2
import numpy as np

WORK = 900  # detection resolution (long side)


def order(p):
    p = np.asarray(p, np.float32).reshape(4, 2)
    s, d = p.sum(1), np.diff(p, axis=1).ravel()
    return np.array([p[np.argmin(s)], p[np.argmin(d)], p[np.argmax(s)], p[np.argmax(d)]], np.float32)


def edge_maps(small):
    lab = cv2.cvtColor(small, cv2.COLOR_BGR2LAB)
    hsv = cv2.cvtColor(small, cv2.COLOR_BGR2HSV)
    maps = []
    for channel in (lab[:, :, 0], lab[:, :, 1], lab[:, :, 2], hsv[:, :, 1]):
        blurred = cv2.bilateralFilter(channel, 9, 40, 9)
        median = float(np.median(blurred))
        for lo_f, hi_f in ((.5, 1.2), (.25, .7)):
            lo = max(5, int(lo_f * median * .5)); hi = max(lo + 10, int(hi_f * median * .5))
            maps.append(cv2.Canny(blurred, lo, hi))
    combined = np.zeros_like(maps[0])
    for m in maps:
        combined |= m
    return maps, combined


def candidate_quads(small, edge_list):
    h, w = small.shape[:2]
    quads = []
    for e in edge_list:
        e = cv2.dilate(e, np.ones((3, 3), np.uint8))
        e = cv2.morphologyEx(e, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
        contours, _ = cv2.findContours(e, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
        for c in sorted(contours, key=cv2.contourArea, reverse=True)[:25]:
            if cv2.contourArea(c) < .015 * w * h:
                break
            hull = cv2.convexHull(c)
            peri = cv2.arcLength(hull, True)
            for tol in (.01, .02, .035, .05):
                ap = cv2.approxPolyDP(hull, tol * peri, True)
                if len(ap) == 4:
                    quads.append(order(ap))
                    break
            else:
                quads.append(order(cv2.boxPoints(cv2.minAreaRect(hull))))
    return quads


def refine_quad(image, q):
    """Snap each side to the strongest nearby straight edge at full resolution."""
    h, w = image.shape[:2]
    lab = cv2.GaussianBlur(cv2.cvtColor(image, cv2.COLOR_BGR2LAB).astype(np.float32), (0, 0), 1.5)
    gxs = np.stack([cv2.Sobel(lab[:, :, c], cv2.CV_32F, 1, 0, ksize=3) / 4 for c in range(3)], -1)
    gys = np.stack([cv2.Sobel(lab[:, :, c], cv2.CV_32F, 0, 1, ksize=3) / 4 for c in range(3)], -1)
    lines = []
    for i in range(4):
        a, b = q[i], q[(i + 1) % 4]
        L = np.linalg.norm(b - a)
        normal = np.array([-(b - a)[1], (b - a)[0]], np.float32) / L
        search = max(4, int(.012 * max(w, h)))
        ts = np.linspace(.08, .92, 60)
        pts = []
        for t in ts:
            base = a + t * (b - a)
            offs = np.arange(-search, search + 1)
            samples = base[None] + offs[:, None] * normal[None]
            xs = np.clip(np.rint(samples[:, 0]).astype(int), 0, w - 1)
            ys = np.clip(np.rint(samples[:, 1]).astype(int), 0, h - 1)
            g = np.abs(gxs[ys, xs] * normal[0] + gys[ys, xs] * normal[1]).max(1)
            if g.max() < 5:
                continue
            # normal points inward: take the outermost strong peak, which is
            # the paper edge rather than printed content just inside it
            strong = np.flatnonzero(g >= .6 * g.max())
            k = int(strong[0])
            while k + 1 < len(g) and g[k + 1] >= g[k]:
                k += 1
            pts.append(samples[k])
        if len(pts) < 15:
            lines.append((a, b)); continue
        pts = np.array(pts, np.float32)
        # robust line fit
        vx, vy, x0, y0 = cv2.fitLine(pts, cv2.DIST_HUBER, 0, .01, .01).ravel()
        d = np.abs((pts[:, 0] - x0) * vy - (pts[:, 1] - y0) * vx)
        keep = d < max(2.0, np.percentile(d, 70))
        if keep.sum() >= 10:
            vx, vy, x0, y0 = cv2.fitLine(pts[keep], cv2.DIST_HUBER, 0, .01, .01).ravel()
        p0 = np.array([x0, y0], np.float32); dv = np.array([vx, vy], np.float32)
        lines.append((p0, p0 + dv))
    out = []
    for i in range(4):
        (a, b), (c, d) = lines[i - 1], lines[i]
        m = np.column_stack((b - a, -(d - c)))
        if abs(np.linalg.det(m)) < 1e-6:
            return q
        t = np.linalg.solve(m, c - a)[0]
        out.append(a + t * (b - a))
    out = order(np.array(out))
    if np.max(np.linalg.norm(out - q, axis=1)) > .04 * max(w, h):
        return q  # refinement jumped to another structure; keep coarse result
    return out

def _dist(a, b):
    """Euclidean distance over the first axis (3 channels), summed in channel order."""
    d = a - b
    s = d[0] * d[0]
    s += d[1] * d[1]
    s += d[2] * d[2]
    return np.sqrt(s)


class Evidence:
    """Directional colour gradients: a side is supported only by edges that
    run along it (wood grain crossing a side does not count)."""

    def __init__(self, small):
        lab = cv2.cvtColor(small, cv2.COLOR_BGR2LAB).astype(np.float32)
        lab = cv2.GaussianBlur(lab, (0, 0), 1.5)
        self.gx = np.stack([cv2.Sobel(lab[:, :, c], cv2.CV_32F, 1, 0, ksize=3) / 4 for c in range(3)], -1)
        self.gy = np.stack([cv2.Sobel(lab[:, :, c], cv2.CV_32F, 0, 1, ksize=3) / 4 for c in range(3)], -1)
        self.lab = cv2.GaussianBlur(lab, (0, 0), 2.5)
        self.h, self.w = small.shape[:2]
        # channel-first flat copies for batched sampling: every per-channel
        # step below runs on contiguous rows (same arithmetic as gx[y, x] ...)
        self._g = np.ascontiguousarray(np.concatenate([self.gx, self.gy], -1).reshape(-1, 6).T)
        self._lab = np.ascontiguousarray(self.lab.reshape(-1, 3).T)

    def flat(self, q):
        """Flat pixel index of the nearest in-image pixel for points (..., 2)."""
        y = np.rint(q[..., 1]).astype(int)
        x = np.rint(q[..., 0]).astype(int)
        np.clip(y, 0, self.h - 1, out=y)
        np.clip(x, 0, self.w - 1, out=x)
        y *= self.w
        y += x
        return y

    def along(self, q, nrm):
        """Strongest channel gradient across a side with normal nrm (..., 2) at points q."""
        g = self._g[:, self.flat(q)]
        nx, ny = nrm[..., 0], nrm[..., 1]
        best = None
        for c in range(3):
            p = g[c] * nx
            p += g[c + 3] * ny
            np.abs(p, out=p)
            best = p if best is None else np.maximum(best, p, out=best)
        return best

    def lab_at(self, q):
        """Smoothed Lab at points q, channel first: (3, ...)."""
        return self._lab[:, self.flat(q)]

    def _idx(self, q):
        return (np.clip(np.rint(q[:, 1]).astype(int), 0, self.h - 1),
                np.clip(np.rint(q[:, 0]).astype(int), 0, self.w - 1))

    def side(self, a, b):
        L = float(np.linalg.norm(b - a))
        n = max(24, int(L / 2.5))
        t = np.linspace(.04, .96, n)
        pts = a[None] + t[:, None] * (b - a)[None]
        nrm = np.array([-(b - a)[1], (b - a)[0]], np.float32) / max(L, 1e-6)
        best = np.zeros(n, np.float32)
        for k in (-2, -1, 0, 1, 2):
            y, x = self._idx(pts + k * nrm)
            proj = np.abs(self.gx[y, x] * nrm[0] + self.gy[y, x] * nrm[1]).max(1)
            best = np.maximum(best, proj)
        band = max(4.0, .012 * max(self.w, self.h))
        yi, xi = self._idx(pts + band * nrm)
        yo, xo = self._idx(pts - band * nrm)
        step = np.linalg.norm(self.lab[yi, xi] - self.lab[yo, xo], axis=1)
        inside_pts = (np.clip(pts[:, 0], 0, self.w - 1) == pts[:, 0]) & (np.clip(pts[:, 1], 0, self.h - 1) == pts[:, 1])
        return float(np.mean(best > 4.0)), float(np.median(step)), float(inside_pts.mean())

    def sides(self, A, B):
        """`side()` for many sides at once (support, median step); same arithmetic, same results."""
        D = B - A
        # per-side norm, as side() takes it, so sample counts match exactly
        L = np.array([float(np.linalg.norm(d)) for d in D])
        n = np.maximum(24, (L / 2.5).astype(np.int64))
        nmax = int(n.max())
        idx = np.arange(nmax)[None]
        valid = idx < n[:, None]
        # np.linspace(.04, .96, n): i * ((.96 - .04) / (n - 1)) + .04, last point exactly .96
        t = idx * ((.96 - .04) / (n[:, None] - 1)) + .04
        t = np.where(idx == n[:, None] - 1, .96, t)
        pts = A[:, None, :] + t[..., None] * D[:, None, :]
        nrm = (np.stack([-D[:, 1], D[:, 0]], 1).astype(np.float32) / np.maximum(L, 1e-6)[:, None].astype(np.float32))[:, None, :]
        best = np.zeros(pts.shape[:2], np.float32)
        for k in (-2, -1, 0, 1, 2):
            best = np.maximum(best, self.along(pts + k * nrm, nrm))
        band = max(4.0, .012 * max(self.w, self.h))
        step = _dist(self.lab_at(pts + band * nrm), self.lab_at(pts - band * nrm))
        support = np.where(valid, best > 4.0, False).sum(1) / n
        median = np.array([np.median(step[r, :n[r]]) for r in range(len(n))])
        return support, median


def line_candidates(small):
    gray = cv2.cvtColor(small, cv2.COLOR_BGR2GRAY)
    lab = cv2.cvtColor(small, cv2.COLOR_BGR2LAB)
    hsv = cv2.cvtColor(small, cv2.COLOR_BGR2HSV)
    lsd = cv2.createLineSegmentDetector(cv2.LSD_REFINE_STD)
    h, w = gray.shape
    segs = []
    for ch in (gray, lab[:, :, 1], lab[:, :, 2], hsv[:, :, 1]):
        found = lsd.detect(cv2.GaussianBlur(ch, (0, 0), 1.2))[0]
        if found is not None:
            segs.extend(found.reshape(-1, 4))
    segs = [s for s in segs if np.hypot(s[2] - s[0], s[3] - s[1]) > .04 * max(h, w)]
    # cluster into infinite lines by (theta, rho)
    lines = []
    for x1, y1, x2, y2 in sorted(segs, key=lambda s: -np.hypot(s[2] - s[0], s[3] - s[1])):
        theta = np.arctan2(y2 - y1, x2 - x1) % np.pi
        nx, ny = -np.sin(theta), np.cos(theta)
        rho = nx * x1 + ny * y1
        length = np.hypot(x2 - x1, y2 - y1)
        for ln in lines:
            dt = min(abs(ln['theta'] - theta), np.pi - abs(ln['theta'] - theta))
            if dt < np.radians(3) and abs(ln['rho'] - rho if abs(ln['theta'] - theta) < np.pi / 2 else ln['rho'] + rho) < .008 * max(h, w):
                ln['length'] += length
                break
        else:
            lines.append(dict(theta=theta, rho=rho, length=length, p=np.array([x1, y1], np.float32),
                              d=np.array([np.cos(theta), np.sin(theta)], np.float32)))
    lines.sort(key=lambda l: -l['length'])
    lines = lines[:60]
    # The photo borders stand in for a paper edge that runs out of frame.
    # They carry no edge evidence (see line_profiles), so a quad may use at
    # most one of them.
    for theta, p in ((0.0, (0, 0)), (0.0, (0, h - 1)), (np.pi / 2, (0, 0)), (np.pi / 2, (w - 1, 0))):
        lines.append(dict(theta=theta, rho=0.0, length=0.0, border=True, p=np.array(p, np.float32),
                          d=np.array([np.cos(theta), np.sin(theta)], np.float32)))
    return lines


def line_profiles(lines, ev):
    """Per-line cumulative edge support / colour step, so any side on that
    line can be scored in O(1)."""
    diag = float(np.hypot(ev.w, ev.h))
    ts = np.arange(-diag, diag, 2.0, dtype=np.float32)
    band = max(4.0, .012 * max(ev.w, ev.h))
    for ln in lines:
        # re-anchor p at the foot of the perpendicular from the image centre
        c = np.array([ev.w / 2, ev.h / 2], np.float32)
        ln['p'] = ln['p'] + float((c - ln['p']) @ ln['d']) * ln['d']
        pts = ln['p'][None] + ts[:, None] * ln['d'][None]
        nrm = np.array([-ln['d'][1], ln['d'][0]], np.float32)
        inside = (pts[:, 0] >= 0) & (pts[:, 0] <= ev.w - 1) & (pts[:, 1] >= 0) & (pts[:, 1] <= ev.h - 1)
        best = np.zeros(len(ts), np.float32)
        for k in (-2, -1, 0, 1, 2):
            best = np.maximum(best, ev.along(pts + k * nrm, nrm))
        step = _dist(ev.lab_at(pts + band * nrm), ev.lab_at(pts - band * nrm))
        ln['t0'] = float(ts[0])
        ln['sup'] = np.concatenate([[0], np.cumsum((best > 4.0) & inside)])
        ln['stp'] = np.concatenate([[0], np.cumsum(np.minimum(step, 60) * inside)])
        ln['ins'] = np.concatenate([[0], np.cumsum(inside & (not ln.get('border', False)))])


def _angle(ta, tb):
    diff = np.abs(ta - tb)
    return np.minimum(diff, np.pi - diff)


def _side_quick_v(SUP, STP, INS, t0, P, D, li, a, b):
    """Vectorised side_quick for many sides on lines li (arrays)."""
    p, d = P[li], D[li]
    ta = ((a - p) * d).sum(1); tb = ((b - p) * d).sum(1)
    lo = np.minimum(ta, tb); hi = np.maximum(ta, tb)
    span = hi - lo
    lo, hi = lo + .05 * span, hi - .05 * span
    last = SUP.shape[1] - 1
    i0 = np.clip((lo - t0) / 2, 0, last).astype(int)
    i1 = np.clip((hi - t0) / 2, 0, last).astype(int)
    n = INS[li, i1] - INS[li, i0]
    ok = n >= 5
    nn = np.where(ok, n, 1)
    sup = np.where(ok, (SUP[li, i1] - SUP[li, i0]) / nn, 0.0)
    stp = np.where(ok, (STP[li, i1] - STP[li, i0]) / nn, 0.0)
    cov = np.where(ok, n / np.maximum(1, i1 - i0), 0.0)
    return sup, stp, cov


def quads_from_lines(lines, shape, keep=150):
    h, w = shape
    n = len(lines)
    if n < 4:
        return []
    TH = np.array([l['theta'] for l in lines], np.float64)
    P = np.array([l['p'] for l in lines], np.float64)
    D = np.array([l['d'] for l in lines], np.float64)
    SUP = np.array([l['sup'] for l in lines], np.float64)
    STP = np.array([l['stp'] for l in lines], np.float64)
    INS = np.array([l['ins'] for l in lines], np.float64)
    t0 = lines[0]['t0']
    ii, jj = np.triu_indices(n, 1)
    nrm = np.stack([-D[ii, 1], D[ii, 0]], 1)
    sep = np.abs(((P[jj] - P[ii]) * nrm).sum(1))
    keep_pair = (_angle(TH[ii], TH[jj]) < np.radians(25)) & (sep > .07 * max(h, w))
    PI, PJ = ii[keep_pair], jj[keep_pair]
    if len(PI) < 2:
        return []
    # pairwise line intersections (Cramer's rule)
    a_ = D[:, None, 0]; b_ = -D[None, :, 0]; c_ = D[:, None, 1]; d_ = -D[None, :, 1]
    det = a_ * d_ - b_ * c_
    rx = P[None, :, 0] - P[:, None, 0]; ry = P[None, :, 1] - P[:, None, 1]
    with np.errstate(divide='ignore', invalid='ignore'):
        t = (rx * d_ - b_ * ry) / det
        X = P[:, None, :] + t[..., None] * D[:, None, :]  # parallel pairs masked by OK
    OK = np.abs(det) >= 1e-3
    A, B = np.triu_indices(len(PI), 1)
    i, j, k, m = PI[A], PJ[A], PI[B], PJ[B]
    sel = (i != k) & (i != m) & (j != k) & (j != m) & (_angle(TH[i], TH[k]) >= np.radians(55))
    sel &= OK[i, k] & OK[i, m] & OK[j, m] & OK[j, k]
    i, j, k, m = i[sel], j[sel], k[sel], m[sel]
    Q = np.stack([X[i, k], X[i, m], X[j, m], X[j, k]], 1)
    margin = .03 * max(h, w)
    inb = ((Q[..., 0] >= -margin) & (Q[..., 0] <= w + margin) &
           (Q[..., 1] >= -margin) & (Q[..., 1] <= h + margin)).all(1)
    i, j, k, m, Q = i[inb], j[inb], k[inb], m[inb], Q[inb]
    if len(Q) == 0:
        return []
    sides = [(i, 0, 1), (m, 1, 2), (j, 2, 3), (k, 3, 0)]
    sup = np.zeros((len(Q), 4)); stp = np.zeros((len(Q), 4)); cov = np.zeros((len(Q), 4))
    for s, (li, x, y) in enumerate(sides):
        sup[:, s], stp[:, s], cov[:, s] = _side_quick_v(SUP, STP, INS, t0, P, D, li, Q[:, x], Q[:, y])
    real = cov > .5
    cnt = real.sum(1)
    ms = np.where(real, sup, np.inf).min(1)
    good = (cnt >= 3) & (ms >= .45)
    mean_sup = np.where(real, sup, 0).sum(1) / np.maximum(cnt, 1)
    min_stp = np.where(real, stp, np.inf).min(1)
    quick = .5 * mean_sup + .3 * ms + .2 * np.minimum(1, min_stp / 35)
    # Quads using a photo border compete in their own small pool so they
    # cannot crowd fully visible documents out of the candidate list.
    full = np.flatnonzero(good & (cnt == 4))
    partial = np.flatnonzero(good & (cnt == 3))
    out = []
    for pool, limit in ((full, keep), (partial, keep // 4)):
        kept = []
        # Near-duplicate lines yield near-identical quads; keep only distinct
        # ones so they cannot use up the candidate budget.
        for q in Q[pool[np.argsort(-quick[pool], kind='stable')]]:
            q = order(q.astype(np.float32))
            if kept and np.abs(np.array(kept) - q).max(axis=(1, 2)).min() < .012 * max(h, w):
                continue
            if cv2.isContourConvex(q.reshape(-1, 1, 2)):
                kept.append(q)
                if len(kept) == limit:
                    break
        out += kept
    return out


def _geometry(q, shape):
    """Cheap shape checks; returns (area, side lengths, border flags) or None."""
    h, w = shape
    area = cv2.contourArea(q) / (w * h)
    if area < .02 or area > 1.02:
        return None
    sides = [np.linalg.norm(q[(i + 1) % 4] - q[i]) for i in range(4)]
    if min(sides) < .07 * max(w, h):
        return None
    for i in range(4):
        u, v = q[i - 1] - q[i], q[(i + 1) % 4] - q[i]
        if abs(float(u @ v / (np.linalg.norm(u) * np.linalg.norm(v)))) > .7:
            return None
    tol = .012 * max(w, h)
    border = []
    for i in range(4):
        a, b = q[i], q[(i + 1) % 4]
        # both ends on the same photo border (a diagonal side merely touching
        # two different borders is still a real paper edge)
        border.append(any(abs(a[k] - edge) < tol and abs(b[k] - edge) < tol
                          for k, edge in ((0, 0), (1, 0), (0, w - 1), (1, h - 1))))
    if sum(border) > 1:
        return None
    return area, sides, border


def evaluate(q, ev, shape):
    return evaluate_many([q], ev, shape)[0]


def evaluate_many(quads, ev, shape):
    """Score candidate quads; geometry first, then all surviving sides in one batch."""
    geo = [_geometry(q, shape) for q in quads]
    keep = [i for i, g in enumerate(geo) if g is not None]
    results = [None] * len(quads)
    if not keep:
        return results
    # batch per dtype so the arithmetic matches side() on each quad exactly
    for dtype in {np.asarray(quads[i]).dtype for i in keep}:
        group = [i for i in keep if np.asarray(quads[i]).dtype == dtype]
        A = np.concatenate([np.asarray(quads[i]) for i in group])
        B = np.concatenate([np.roll(np.asarray(quads[i]), -1, axis=0) for i in group])
        support, median = ev.sides(A, B)
        for j, i in enumerate(group):
            area, sides, border = geo[i]
            sup = [float(v) for v in support[4 * j:4 * j + 4]]
            steps = [float(v) for v in median[4 * j:4 * j + 4]]
            results[i] = _score(area, sides, border, sup, steps)
    return results


def _score(area, sides, border, sup, steps):
    if sum(border) > 1:
        return None
    if any(border):
        # A sheet that leaves the frame still shows a large, sheet-shaped
        # part; a thin strip against the border is desk or keyboard.
        lengths = sorted(sides)
        if area < .15 or lengths[1] < .25 * lengths[3]:
            return None
    real = [i for i in range(4) if not border[i]]
    min_sup = min(sup[i] for i in real)
    min_step = min(steps[i] for i in real)
    if min_sup < .5 or min_step < 6:
        return None
    mean_sup = float(np.mean([sup[i] for i in real]))
    score = .45 * mean_sup + .30 * min_sup + .25 * min(1.0, min_step / 35) + .10 * np.sqrt(min(area, 1))
    if any(border):
        score -= .05
    return score, dict(area=round(area, 3), support=[round(s, 2) for s in sup],
                       step=[round(s, 1) for s in steps], border=border)


def paper_reference(q, ev):
    """Median colour of the quad's central area, if it looks like plain paper."""
    c = q.mean(0)
    inner = c + .6 * (q - c)
    xs, ys = np.meshgrid(np.linspace(0, 1, 24), np.linspace(0, 1, 24))
    top = inner[0] + xs[..., None] * (inner[1] - inner[0])
    bot = inner[3] + xs[..., None] * (inner[2] - inner[3])
    pts = (top + ys[..., None] * (bot - top)).reshape(-1, 2)
    y, x = ev._idx(pts)
    vals = ev.lab[y, x]
    ref = np.median(vals, 0)
    chroma = np.hypot(vals[:, 1] - 128, vals[:, 2] - 128)
    return ref if np.median(chroma) < 14 and ref[0] > 110 else None


def _side_fit_v(ev, A, B, inward, paper_l):
    """side_fit for many candidate sides (A, B: (N,2)) sharing one inward direction."""
    L = np.linalg.norm(B - A, axis=1)
    n = np.maximum(30, (L / 3).astype(int))
    nmax = int(n.max())
    # per-row sampling with its own count: build padded parameter grid
    idx = np.arange(nmax)[None]
    valid = idx < n[:, None]
    t = .05 + .9 * idx / np.maximum(n[:, None] - 1, 1)
    pts = A[:, None] + t[..., None] * (B - A)[:, None]
    nrm = np.stack([-(B - A)[:, 1], (B - A)[:, 0]], 1) / np.maximum(L, 1e-6)[:, None]
    flip = (nrm @ inward) < 0
    nrm[flip] *= -1
    nrm = nrm[:, None]
    sup = np.zeros(pts.shape[:2], np.float32)
    for kk in (-1, 0, 1):
        sup = np.maximum(sup, ev.along(pts + kk * nrm, nrm))
    band = max(4.0, .012 * max(ev.w, ev.h))
    inner = ev.lab_at(pts + 1.5 * band * nrm)
    din = _dist(inner, ev.lab_at(pts + 4.0 * band * nrm))
    # paper-like: low chroma, not much darker than the page (a shaded fold
    # panel still counts; dark gaps between keys do not), and similar a
    # little deeper inside
    din = np.where((np.hypot(inner[1] - 128, inner[2] - 128) < 16) &
                   (inner[0] > .7 * paper_l), din, 99)
    dout = _dist(ev.lab_at(pts - 1.0 * band * nrm), inner)
    inside_ok = np.clip(pts[..., 0], 0, ev.w - 1) == pts[..., 0]
    cnt = n.astype(np.float64)
    mean = lambda v: np.where(valid, v, 0).sum(1) / cnt
    return (.45 * mean(sup > 4) + .35 * mean(din < 20) + .20 * mean(dout > 8)) * (.6 + .4 * mean(inside_ok))


def adjust_sides(q, ev):
    ref = paper_reference(q, ev)
    if ref is None:
        return q
    q = q.copy()
    c = q.mean(0)
    offs = np.linspace(-.04, .15, 40)
    OA, OB = np.meshgrid(offs, offs, indexing='ij')
    grid = np.abs(OA - OB) <= .06
    OA, OB = OA[grid], OB[grid]
    for _ in range(2):
        for i in range(4):
            a_i, b_i = i, (i + 1) % 4
            prev_dir = q[a_i] - q[(i - 1) % 4]
            next_dir = q[b_i] - q[(i + 2) % 4]
            inward = c - (q[a_i] + q[b_i]) / 2
            base = float(_side_fit_v(ev, q[a_i][None].astype(np.float64), q[b_i][None].astype(np.float64), inward, ref[0])[0])
            PA = q[a_i][None] - OA[:, None] * prev_dir[None]
            PB = q[b_i][None] - OB[:, None] * next_dir[None]
            scores = _side_fit_v(ev, PA.astype(np.float64), PB.astype(np.float64), inward, ref[0])
            best = (base, q[a_i], q[b_i])
            for s, pa, pb in zip(scores, PA, PB):
                if s > best[0] + .03:
                    best = (s, pa, pb)
            q[a_i], q[b_i] = best[1], best[2]
    return order(q)


def merge_folded_panels(scored, ev, shape):
    """Join paper panels that share a side (a fold crease) into one sheet.

    A folded receipt or letter is not planar, so its outer edges bend at each
    fold and no single straight-sided quad fits it; each flat panel between
    creases does. Panels qualify only when both look like plain paper.
    """
    h, w = shape
    tol = .03 * max(h, w)
    panels = []
    for s in scored:
        if len(panels) == 25 or s[0] <= scored[0][0] - .15:
            break
        # a panel borrowing a photo border is not evidence of a sheet edge
        if any(s[2]['border']) or paper_reference(s[1], ev) is None:
            continue
        if all(np.abs(s[1] - p[1]).max() >= tol for p in panels):
            panels.append(s)
    merged = []
    for a in panels:
        for b in panels:
            if a is b:
                continue
            for i in range(4):
                a0, a1 = a[1][i], a[1][(i + 1) % 4]
                for j in range(4):
                    b0, b1 = b[1][j], b[1][(j + 1) % 4]
                    # adjacent quads traverse a shared side in opposite directions
                    if np.linalg.norm(a0 - b1) > tol or np.linalg.norm(a1 - b0) > tol:
                        continue
                    union = order(np.array([a[1][(i + 2) % 4], a[1][(i + 3) % 4],
                                            b[1][(j + 2) % 4], b[1][(j + 3) % 4]]))
                    if not cv2.isContourConvex(union.reshape(-1, 1, 2)):
                        continue
                    area = cv2.contourArea(union) / (w * h)
                    if area < (a[2]['area'] + b[2]['area']) * .9:
                        continue
                    if any(np.abs(union - m[1]).max() < tol for m in merged):
                        continue
                    info = dict(a[2], area=round(area, 3), border=[False] * 4, folded=True)
                    merged.append((min(a[0], b[0]), union, info))
    return merged


def detect_document(image):
    h, w = image.shape[:2]
    scale = WORK / max(h, w)
    small = cv2.resize(image, (round(w * scale), round(h * scale)), interpolation=cv2.INTER_AREA)
    ev = Evidence(small)
    edge_list, combined = edge_maps(small)
    lines = line_candidates(small)
    line_profiles(lines, ev)
    cands = quads_from_lines(lines, small.shape[:2])
    cands += candidate_quads(small, edge_list + [combined])
    scored = [(r[0], q, r[1]) for q, r in zip(cands, evaluate_many(cands, ev, small.shape[:2])) if r]
    if not scored:
        return None, 0.0
    scored.sort(key=lambda s: -s[0])
    # Prefer an enclosing candidate when it is nearly as well supported:
    # the outer paper edge beats a printed frame inside it.
    best = top = scored[0]
    for s in scored[1:]:
        # Compare with the top score, so repeated hops cannot drift downwards.
        # A quad borrowing a photo border must win on its own score. Every
        # side of the larger quad must be a continuous edge: a printed frame
        # sits inside a real sheet edge, whereas a quad stretched to a
        # keyboard or desk seam crosses stretches with no edge at all.
        if not any(s[2]['border']) and s[2]['area'] > best[2]['area'] * 1.15 and s[0] > top[0] - .06 and \
                min(s[2]['support']) >= .9 and \
                cv2.pointPolygonTest(s[1].reshape(-1, 1, 2), tuple(map(float, best[1].mean(0))), False) > 0 and \
                all(cv2.pointPolygonTest(s[1].reshape(-1, 1, 2), tuple(map(float, p)), True) > -3 for p in best[1]):
            best = s
    # Folded sheets: repeatedly join panels (two creases make three panels)
    # and prefer the largest sheet that contains the best panel.
    pool = list(scored)
    for _ in range(3):
        joined = merge_folded_panels(pool, ev, small.shape[:2])
        if not joined:
            break
        pool = sorted(pool + joined, key=lambda s: -s[0])
        for s in joined:
            if s[2]['area'] > best[2]['area'] and \
                    cv2.pointPolygonTest(s[1].reshape(-1, 1, 2), tuple(map(float, best[1].mean(0))), False) > 0:
                best = s
    if best[2].get('folded'):
        # A folded sheet's sides bend at each crease, so straight-side
        # adjustment would pull the corners inwards; keep the panel corners.
        quad = best[1] / scale
    else:
        quad = refine_quad(image, adjust_sides(best[1], ev) / scale)
    quad = np.clip(quad, 0, [w - 1, h - 1]).astype(np.float32)
    return quad, float(best[0])


def true_aspect(q, shape):
    """Width/height of the physical rectangle (Zhang & He, unknown focal length)."""
    h, w = shape
    tl, tr, br, bl = q
    side = (np.linalg.norm(tr - tl) + np.linalg.norm(br - bl)) / (np.linalg.norm(bl - tl) + np.linalg.norm(br - tr))
    u0, v0 = w / 2, h / 2
    m1, m2, m3, m4 = [np.array([p[0], p[1], 1.0]) for p in (tl, tr, bl, br)]
    try:
        k2 = np.dot(np.cross(m1, m4), m3) / np.dot(np.cross(m2, m4), m3)
        k3 = np.dot(np.cross(m1, m4), m2) / np.dot(np.cross(m3, m4), m2)
        n2 = k2 * m2 - m1; n3 = k3 * m3 - m1
        # A phone's main camera has a focal length of roughly 0.6x the image
        # diagonal. The rectangle only determines f when both pairs of sides
        # converge; with one parallel pair (tilt about a single axis) use it.
        diag = float(np.hypot(w, h))
        f2 = (.6 * diag) ** 2
        if abs(n2[2] * n3[2]) > 1e-9:
            measured = -((n2[0] * n3[0] - (n2[0] * n3[2] + n2[2] * n3[0]) * u0 + n2[2] * n3[2] * u0 ** 2) +
                         (n2[1] * n3[1] - (n2[1] * n3[2] + n2[2] * n3[1]) * v0 + n2[2] * n3[2] * v0 ** 2)) / (n2[2] * n3[2])
            if np.isfinite(measured) and (.3 * diag) ** 2 < measured < (3 * diag) ** 2:
                f2 = measured
        A = np.array([[np.sqrt(f2), 0, u0], [0, np.sqrt(f2), v0], [0, 0, 1]])
        Ai = np.linalg.inv(A); B = Ai.T @ Ai
        ratio = np.sqrt((n2 @ B @ n2) / (n3 @ B @ n3))
        if not np.isfinite(ratio) or abs(ratio / side - 1) > .25:
            return side
        return float(ratio)
    except (np.linalg.LinAlgError, ZeroDivisionError, FloatingPointError):
        return side


def warp(image, q, long_side=2400):
    aspect = true_aspect(q, image.shape[:2])
    # snap to common paper sizes when within 2.5%
    for std in (1 / 1.4142, 1 / 1.2941, 1.4142, 1.2941):
        if abs(aspect / std - 1) < .025:
            aspect = std
    src_long = max(np.linalg.norm(q[1] - q[0]), np.linalg.norm(q[3] - q[0]))
    L = max(long_side, int(src_long))
    out_w, out_h = (L, round(L / aspect)) if aspect >= 1 else (round(L * aspect), L)
    dst = np.array([[0, 0], [out_w - 1, 0], [out_w - 1, out_h - 1], [0, out_h - 1]], np.float32)
    M = cv2.getPerspectiveTransform(q.astype(np.float32), dst)
    return cv2.warpPerspective(image, M, (out_w, out_h), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)


def paper_background(gray):
    """Illumination field: close away ink, then smooth."""
    h, w = gray.shape
    s = 1000 / max(h, w)
    small = cv2.resize(gray, (round(w * s), round(h * s)), interpolation=cv2.INTER_AREA)
    k = max(15, int(.035 * max(small.shape))) | 1
    bg = cv2.morphologyEx(small, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k)))
    bg = cv2.medianBlur(bg, 21)
    bg = cv2.GaussianBlur(bg.astype(np.float32), (0, 0), k / 3)
    return cv2.resize(bg, (w, h), interpolation=cv2.INTER_CUBIC)


def is_text_document(img):
    hsv = cv2.cvtColor(cv2.resize(img, (400, round(400 * img.shape[0] / img.shape[1]))), cv2.COLOR_BGR2HSV)
    bright_low_sat = (hsv[:, :, 1] < 60) & (hsv[:, :, 2] > 110)
    return float(bright_low_sat.mean()) > .55


def clean_border(out, frac=.025):
    """Whiten dark components that touch the page border (desk/fold slivers)."""
    h, w = out.shape[:2]
    gray = out if out.ndim == 2 else cv2.cvtColor(out, cv2.COLOR_BGR2GRAY)
    ink = np.uint8(gray < 200)
    n, labels, stats, _ = cv2.connectedComponentsWithStats(ink, 8)
    band = int(frac * min(h, w))
    thin = max(3, int(.012 * min(h, w)))
    # Same per-component rule, evaluated on all components at once.
    x, y, bw, bh, a = (stats[:, k].astype(np.int64) for k in range(5))
    touches = (x <= 2) | (y <= 2) | (x + bw >= w - 2) | (y + bh >= h - 2)
    in_thin = (x + bw <= thin) | (y + bh <= thin) | (x >= w - thin) | (y >= h - thin)
    within_band = (x + bw <= band * 3) | (y + bh <= band * 3) | (x >= w - band * 3) | (y >= h - band * 3)
    large = (a > .002 * h * w) & ((bw < band * 4) | (bh < band * 4))
    kill = in_thin | (touches & (within_band | large))
    kill[0] = False
    mask = kill[labels]
    out = out.copy()
    out[mask] = 255
    # isolated clusters confined to the left/right margin (staples, holes, fold debris)
    gray = out if out.ndim == 2 else cv2.cvtColor(out, cv2.COLOR_BGR2GRAY)
    ink = np.uint8(gray < 215)
    grouped = cv2.dilate(ink, np.ones((int(.012 * w) | 1,) * 2, np.uint8))
    n, labels, stats, _ = cv2.connectedComponentsWithStats(grouped, 8)
    for i in range(1, n):
        x, y, bw, bh, a = stats[i]
        if (x + bw <= .085 * w or x >= .915 * w) and bh < .12 * h:
            # work inside the cluster's bounding box only (same pixels as a
            # whole-image comparison, without scanning the page per cluster)
            box = (slice(y, y + bh), slice(x, x + bw))
            region = labels[box] == i
            ink_vals = gray[box][region & (ink[box] > 0)]
            # dark, crisp marks in the margin are content (page numbers,
            # notes); only faint grey debris (staple scars, fold shading) goes
            if ink_vals.size and float(np.percentile(ink_vals, 10)) > 95:
                out[box][region] = 255
    edge = max(2, int(.005 * min(h, w)))
    out[:edge] = 255; out[-edge:] = 255; out[:, :edge] = 255; out[:, -edge:] = 255
    return out


def remove_margin_holes(out):
    """Remove isolated round blobs (punch holes) in the left/right/top margins."""
    h, w = out.shape[:2]
    gray = out if out.ndim == 2 else cv2.cvtColor(out, cv2.COLOR_BGR2GRAY)
    ink = np.uint8(gray < 170)
    n, labels, stats, cents = cv2.connectedComponentsWithStats(ink, 8)
    out = out.copy()
    for i in range(1, n):
        x, y, bw, bh, a = stats[i]
        cx, cy = cents[i]
        in_margin = cx < .12 * w or cx > .88 * w
        size_ok = .004 * w < max(bw, bh) < .06 * w and .6 < bw / max(bh, 1) < 1.67
        if not (in_margin and size_ok):
            continue
        # A hole (or its shadow crescent) is solid; a hollow ring is a printed
        # character such as 0 or O and must be kept.
        blob = np.uint8(labels[y:y + bh, x:x + bw] == i)
        _, hierarchy = cv2.findContours(blob, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
        if hierarchy is not None and (hierarchy[0][:, 3] >= 0).any():
            continue
        # isolated: no other ink in its neighbourhood
        pad = int(.03 * w)
        y0, y1, x0, x1 = max(0, y - pad), min(h, y + bh + pad), max(0, x - pad), min(w, x + bw + pad)
        region = labels[y0:y1, x0:x1]
        others = np.count_nonzero((region != i) & (region != 0))
        if others < .15 * a:
            # dilate inside the bounding box plus the 2 px the 5x5 kernel reaches
            by0, by1, bx0, bx1 = max(0, y - 2), min(h, y + bh + 2), max(0, x - 2), min(w, x + bw + 2)
            m = cv2.dilate(np.uint8(labels[by0:by1, bx0:bx1] == i), np.ones((5, 5), np.uint8)).astype(bool)
            out[by0:by1, bx0:bx1][m] = 255
    return out


def magic_text(img):
    """CamScanner-style clean text page: white paper, crisp dark ink, colour ink kept."""
    lab = cv2.cvtColor(img, cv2.COLOR_BGR2LAB)
    L = lab[:, :, 0].astype(np.float32)
    bg = paper_background(lab[:, :, 0])
    ratio = L / np.maximum(bg, 1)
    # ink statistics decide the black point; paper and show-through go white
    ink_vals = ratio[ratio < .75]
    black = float(np.percentile(ink_vals, 20)) if ink_vals.size > 500 else .35
    white = .86
    t = np.clip((ratio - black) / (white - black), 0, 1)
    t = t ** 1.35  # darken mid-tones of strokes, keep anti-aliasing
    newL = 255 * t
    # colour: keep chroma only where there is real ink with colour
    a = lab[:, :, 1].astype(np.float32) - 128
    b = lab[:, :, 2].astype(np.float32) - 128
    chroma = np.sqrt(a * a + b * b)
    keep = np.clip((chroma - 12) / 18, 0, 1) * (1 - t)
    out_lab = np.dstack([newL, 128 + a * keep * 1.3, 128 + b * keep * 1.3])
    out = cv2.cvtColor(np.clip(out_lab, 0, 255).astype(np.uint8), cv2.COLOR_LAB2BGR)
    blur = cv2.GaussianBlur(out, (0, 0), 1.0)
    out = cv2.addWeighted(out, 1.5, blur, -.5, 0)
    out = clean_border(out)
    out = remove_margin_holes(out)
    return out


def magic_color(img):
    """Colourful cards/photos: white balance, lift shadows, contrast, saturation."""
    f = img.astype(np.float32)
    # gray-world on the brightest 10% as white reference
    lum = f.mean(2)
    ref = f[lum >= np.percentile(lum, 92)].mean(0)
    f *= (ref.max() / np.maximum(ref, 1))[None, None]
    # illumination flattening on V (gentle, preserves colours)
    hsv = cv2.cvtColor(np.clip(f, 0, 255).astype(np.uint8), cv2.COLOR_BGR2HSV).astype(np.float32)
    v = hsv[:, :, 2]
    # The lighting field is very smooth, so estimate it at 1/8 resolution.
    small_v = cv2.resize(v, (max(1, v.shape[1] // 8), max(1, v.shape[0] // 8)), interpolation=cv2.INTER_AREA)
    bg = cv2.resize(cv2.GaussianBlur(small_v, (0, 0), max(small_v.shape) / 12),
                    (v.shape[1], v.shape[0]), interpolation=cv2.INTER_LINEAR)
    v = np.clip(v * (np.percentile(bg, 90) / np.maximum(bg, 1)) ** .6, 0, 255)
    lo, hi = np.percentile(v, 1), np.percentile(v, 99.2)
    v = np.clip((v - lo) / max(hi - lo, 1), 0, 1) * 255
    hsv[:, :, 2] = v
    hsv[:, :, 1] = np.clip(hsv[:, :, 1] * 1.15, 0, 255)
    out = cv2.cvtColor(hsv.astype(np.uint8), cv2.COLOR_HSV2BGR)
    lab = cv2.cvtColor(out, cv2.COLOR_BGR2LAB)
    lab[:, :, 0] = cv2.createCLAHE(1.5, (8, 8)).apply(lab[:, :, 0])
    out = cv2.cvtColor(lab, cv2.COLOR_LAB2BGR)
    blur = cv2.GaussianBlur(out, (0, 0), 1.2)
    return cv2.addWeighted(out, 1.4, blur, -.4, 0)


def render_magic(flat):
    """Text pages get the clean black-on-white treatment; colourful pages keep colour."""
    return magic_text(flat) if is_text_document(flat) else magic_color(flat)
