"""Regenerates the reference artefacts of the visual tests, and their manifest entries, from the tools
recorded for them.

    python -I scripts/visual-references.py photographs   # the photographs Pragmatic.Imaging.Tests encodes
    python -I scripts/visual-references.py metrics       # the image pairs and the values the metrics are held to

Needs scikit-image, Pillow and NumPy (pip install scikit-image pillow, in a virtual environment: docs/TESTING.md,
"Images and PDF pages, compared visually"); `metrics` also downloads the CIEDE2000 test data from scikit-image's
repository at the tag of the installed version.

The rule this exists for: a reference is never produced by the code it checks. Every file written here
comes from scikit-image, Pillow or NumPy, never from a Pragmatic assembly, and the manifest beside it
(`references.json`) says which tool, which version and what was done. No test runs this script: a
regenerated reference is a change to commit, and shows up in the diff of the pull request.
"""
import datetime
import json
import shutil
import sys
import urllib.request
from pathlib import Path

import numpy as np
import PIL
import skimage
from PIL import Image, ImageFilter
from skimage import color, data, metrics

ROOT = Path(__file__).resolve().parent.parent
PHOTOGRAPHS = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Tests' / 'References' / 'Photographs'
METRICS = ROOT / 'shared' / 'VisualTesting.Tests' / 'References'
TODAY = datetime.date.today().isoformat()
TOOLS = f'scikit-image {skimage.__version__}, Pillow {PIL.__version__}, NumPy {np.__version__}'
SKIMAGE_DATA = Path(data.__file__).parent

# Each photograph with the licence its own docstring in skimage.data states, quoted.
SOURCES = {
    'astronaut': ('astronaut.png', 'NASA, the astronaut Eileen Collins',
                  'Public domain: "No known copyright restrictions, released into the public domain."'),
    'coffee': ('coffee.png', 'Rachel Michetti, courtesy of Pikolo Espresso Bar',
               'CC0: "No copyright restrictions. CC0 by the photographer (Rachel Michetti)."'),
    'chelsea': ('chelsea.png', 'Stefan van der Walt',
                'CC0: "No copyright restrictions. CC0 by the photographer (Stefan van der Walt)."'),
    'rocket': ('rocket.jpg', 'SpaceX, the launch of DSCOVR on Falcon 9',
               'Public domain: "The image was captured by SpaceX and released in the public domain."'),
}


def write_manifest(directory, entries):
    manifest = {
        'about': 'Every reference in this directory, with what produced it. Regenerate with '
                 'scripts/visual-references.py; a reference is never produced by the code it checks.',
        'references': dict(sorted(entries.items())),
    }
    (directory / 'references.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def photographs():
    PHOTOGRAPHS.mkdir(parents=True, exist_ok=True)
    entries = {}
    for name, (file, author, licence) in SOURCES.items():
        source = SKIMAGE_DATA / file
        target = PHOTOGRAPHS / f'{name}.png'
        if source.suffix == '.png':
            shutil.copyfile(source, target)
            how = f'copied byte for byte from skimage/data/{file}'
        else:
            Image.open(source).convert('RGB').save(target, optimize=True)
            how = f'skimage/data/{file} decoded by Pillow and written as PNG (the source is itself a JPEG)'
        entries[target.name] = {
            'origin': f'scikit-image {skimage.__version__}, skimage.data.{name}: {author}',
            'licence': licence,
            'producedBy': f'scripts/visual-references.py photographs, {TOOLS}: {how}',
            'date': TODAY,
        }
    write_manifest(PHOTOGRAPHS, entries)


def luma(rgb):
    """Rec. 601 luma, the weights JPEG uses, in floating point over 0..255."""
    rgb = rgb.astype(np.float64)
    return 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]


def ssim(a, b):
    return float(metrics.structural_similarity(a, b, data_range=255, gaussian_weights=True, sigma=1.5,
                                               use_sample_covariance=False))


def measure(reference, distorted):
    """What the C# metrics are held to, each computed by scikit-image or NumPy."""
    difference = np.abs(reference.astype(np.int32) - distorted.astype(np.int32))
    delta_e = color.deltaE_ciede2000(color.rgb2lab(reference), color.rgb2lab(distorted))
    return {
        'maxAbsolute': [int(difference[..., c].max()) for c in range(3)],
        'meanAbsolute': [float(difference[..., c].mean()) for c in range(3)],
        'psnr': float(metrics.peak_signal_noise_ratio(reference, distorted, data_range=255)),
        'ssimLuma': ssim(luma(reference), luma(distorted)),
        'ssimChannels': [ssim(reference[..., c].astype(np.float64), distorted[..., c].astype(np.float64)) for c in range(3)],
        'deltaE2000Mean': float(delta_e.mean()),
        'deltaE2000Max': float(delta_e.max()),
    }


def metric_fixtures():
    METRICS.mkdir(parents=True, exist_ok=True)
    entries = {}
    values = {}
    # Two crops of 128 x 128: skin and hair, then porcelain against wood grain.
    crops = {'astronaut-face': (data.astronaut(), 100, 160), 'coffee-texture': (data.coffee(), 150, 230)}
    rng = np.random.default_rng(1014)
    for name, (image, top, left) in crops.items():
        original = np.ascontiguousarray(image[top:top + 128, left:left + 128, :3])
        source = name.split('-')[0]
        variants = {'original': original}

        pil = Image.fromarray(original)
        jpeg_path = METRICS / f'{name}.jpeg-q25.jpg'
        pil.save(jpeg_path, quality=25)
        variants['jpeg-q25'] = np.asarray(Image.open(jpeg_path).convert('RGB'))
        jpeg_path.unlink()
        variants['blur'] = np.asarray(pil.filter(ImageFilter.GaussianBlur(radius=1.5)))
        noise = rng.integers(-12, 13, size=original.shape)
        variants['noise'] = np.clip(original.astype(np.int32) + noise, 0, 255).astype(np.uint8)

        how = {
            'original': f'a 128 x 128 crop at row {top}, column {left} of skimage.data.{source}',
            'jpeg-q25': 'the original encoded by Pillow as JPEG at quality 25, then decoded by Pillow',
            'blur': 'the original through Pillow ImageFilter.GaussianBlur(radius=1.5)',
            'noise': 'the original plus NumPy default_rng(1014).integers(-12, 13) per sample, clipped to 0..255',
        }
        for variant, pixels in variants.items():
            file = f'{name}.{variant}.png'
            Image.fromarray(pixels).save(METRICS / file)
            entries[file] = {
                'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}',
                'licence': SOURCES[source][2],
                'producedBy': f'scripts/visual-references.py metrics, {TOOLS}: {how[variant]}',
                'date': TODAY,
            }
            if variant != 'original':
                values[f'{name}.original.png|{file}'] = measure(original, pixels)

    (METRICS / 'metrics.json').write_text(json.dumps({
        'about': 'The value of every metric on every pair, computed by the tools named here, which the C# metrics must match.',
        'tools': TOOLS,
        'luma': 'Rec. 601: 0.299 R + 0.587 G + 0.114 B',
        'ssim': 'skimage.metrics.structural_similarity(data_range=255, gaussian_weights=True, sigma=1.5, use_sample_covariance=False)',
        'psnr': 'skimage.metrics.peak_signal_noise_ratio(data_range=255), over the three channels',
        'deltaE2000': 'skimage.color.deltaE_ciede2000 over skimage.color.rgb2lab (D65, 2 degrees)',
        'pairs': values,
    }, indent=2) + '\n', encoding='utf-8')
    entries['metrics.json'] = {
        'origin': 'computed on the image pairs in this directory',
        'licence': 'derived values, no licence of their own',
        'producedBy': f'scripts/visual-references.py metrics, {TOOLS}',
        'date': TODAY,
    }

    tag = f'v{skimage.__version__}'
    url = f'https://raw.githubusercontent.com/scikit-image/scikit-image/{tag}/tests/skimage/color/ciede2000_test_data.txt'
    with urllib.request.urlopen(url) as response:
        (METRICS / 'ciede2000_test_data.txt').write_bytes(response.read())
    entries['ciede2000_test_data.txt'] = {
        'origin': 'G. Sharma, W. Wu, E. N. Dalal, "The CIEDE2000 Color-Difference Formula: Implementation Notes, '
                  'Supplementary Test Data, and Mathematical Observations", Color Research and Application 30(1), 2005; '
                  f'as transcribed by scikit-image at {tag}, tests/skimage/color/ciede2000_test_data.txt',
        'licence': 'published test data; the transcription is part of scikit-image, BSD-3-Clause',
        'producedBy': f'scripts/visual-references.py metrics: downloaded from {url}',
        'date': TODAY,
    }
    write_manifest(METRICS, entries)


COMMANDS = {'photographs': photographs, 'metrics': metric_fixtures}

if __name__ == '__main__':
    if len(sys.argv) != 2 or sys.argv[1] not in COMMANDS:
        sys.exit(f'usage: python -I scripts/visual-references.py {{{"|".join(COMMANDS)}}}')
    COMMANDS[sys.argv[1]]()
