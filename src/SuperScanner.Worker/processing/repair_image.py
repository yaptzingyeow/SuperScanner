"""Bounded CLI for private hole suggestions and user-reviewed cleanup previews."""
import os
os.environ['OPENCV_IO_MAX_IMAGE_PIXELS'] = '4000000'
os.environ['OPENBLAS_NUM_THREADS'] = '1'
os.environ['OMP_NUM_THREADS'] = '1'

import json
import sys

if sys.platform != 'win32':
    import resource
    resource.setrlimit(resource.RLIMIT_AS, (1536 * 1024**2, 1536 * 1024**2))
    resource.setrlimit(resource.RLIMIT_CPU, (20, 20))

import cv2
import numpy as np
from hole_candidates import find_hole_candidates
from masked_repair import repair_masked_image

cv2.setNumThreads(1)


def _read_image(path):
    if os.path.getsize(path) > 25 * 1024 * 1024:
        raise ValueError('Source image is too large')
    image = cv2.imread(path, cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError('Source image is invalid')
    return image


def _read_payload(argument):
    if not argument.startswith('@'):
        return json.loads(argument)
    path = argument[1:]
    if os.path.getsize(path) > 512 * 1024:
        raise ValueError('Repair request is too large')
    with open(path, encoding='utf-8') as file:
        return json.load(file)


def main(arguments):
    if len(arguments) in (2, 3) and arguments[0] == 'detect':
        image = _read_image(arguments[1])
        protected = _read_payload(arguments[2]) if len(arguments) == 3 else []
        if not isinstance(protected, list) or len(protected) > 2000:
            raise ValueError('Invalid protected areas')
        print(json.dumps({'candidates': [[float(value) for value in box]
            for box in find_hole_candidates(image, protected)]}))
        return 0
    if len(arguments) == 4 and arguments[0] == 'preview':
        image = _read_image(arguments[1])
        request = _read_payload(arguments[3])
        if (not isinstance(request, dict) or
                not {'rectangles', 'protected'} <= set(request) or
                set(request) - {'rectangles', 'protected', 'normalized', 'strokes'}):
            raise ValueError('Invalid repair request')
        if request.get('normalized', False):
            width, height = image.shape[1], image.shape[0]
            def pixels(items):
                result = []
                for item in items:
                    if (not isinstance(item, list) or len(item) != 4 or
                            any(not isinstance(value, (float, int)) or
                                not 0 <= value <= 1 for value in item)):
                        raise ValueError('Invalid normalized repair area')
                    result.append((int(item[0] * width), int(item[1] * height),
                                   int(np.ceil(item[2] * width)), int(np.ceil(item[3] * height))))
                return result
            rectangles = pixels(request['rectangles'])
            protected = pixels(request['protected'])
            strokes = []
            for stroke in request.get('strokes', []):
                if (not isinstance(stroke, dict) or set(stroke) != {'radius', 'points'} or
                        not isinstance(stroke['radius'], (float, int)) or
                        not 0 < stroke['radius'] <= .05 or
                        not isinstance(stroke['points'], list) or
                        not 1 <= len(stroke['points']) <= 256):
                    raise ValueError('Invalid normalized brush stroke')
                points = []
                for point in stroke['points']:
                    if (not isinstance(point, list) or len(point) != 2 or
                            any(not isinstance(value, (float, int)) or
                                not 0 <= value <= 1 for value in point)):
                        raise ValueError('Invalid normalized brush point')
                    points.append((min(width - 1, int(point[0] * width)),
                                   min(height - 1, int(point[1] * height))))
                strokes.append((max(1, round(stroke['radius'] * min(width, height))), points))
        else:
            rectangles = [tuple(item) for item in request['rectangles']]
            protected = [tuple(item) for item in request['protected']]
            strokes = [(item['radius'], [tuple(point) for point in item['points']])
                       for item in request.get('strokes', [])]
        repaired = repair_masked_image(image, rectangles, protected, strokes)
        if not arguments[2].lower().endswith('.png') or not cv2.imwrite(arguments[2], repaired):
            raise ValueError('Could not write preview')
        print(json.dumps({'width': image.shape[1], 'height': image.shape[0]}))
        return 0
    raise ValueError('Expected detect SOURCE or preview SOURCE OUTPUT REQUEST_JSON')


if __name__ == '__main__':
    try:
        sys.exit(main(sys.argv[1:]))
    except (ValueError, TypeError, KeyError, IndexError, OSError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(2)
