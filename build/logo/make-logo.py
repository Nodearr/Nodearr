"""Draws Nodearr's mark and everything made from it.

    python build/logo/make-logo.py

The mark: two arcs round a node. The left arc is yours and the right a friend's, and the node
between them is split in two, each half the colour of the arc it faces: what friends share
belongs to both. It sits on a disc, as the Arr apps' marks do.

Writes the SVGs here: Logo/Nodearr.svg (light disc), Logo/Nodearr-dark.svg (dark disc, for dark
backgrounds), the web UI's logo and Safari mask, and the README banners. The PNG and ICO files are
rendered from them by build/logo/render-icons.mjs.
"""

import math
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

LIGHT = '#EEF2F3'
DARK = '#1E2A32'
TEAL = '#14B8A6'

# The shape, in a 512 x 512 square centred on 256, 256.
RADIUS = 162     # of the arcs' centre line
WIDTH = 54       # of the arcs
GAP = 84         # degrees left open between the arcs, top and bottom
NODE = 74        # radius of the node
SLIVER = 7       # half the gap between the node's halves
TILT = -35       # degrees the whole is turned


def point(r, degrees):
    a = math.radians(degrees)
    return 256 + r * math.cos(a), 256 + r * math.sin(a)


def mark(yours, friends, *, prefix='', node_yours=None, node_friends=None):
    """The arcs and the node, without the disc. ids are prefixed so two marks can share a page."""
    node_yours = node_yours or yours
    node_friends = node_friends or friends
    half = 90 - GAP / 2
    l1, l2 = point(RADIUS, 180 - half), point(RADIUS, 180 + half)
    r1, r2 = point(RADIUS, -half), point(RADIUS, half)
    n, s = NODE, SLIVER

    return f'''<defs>
    <clipPath id="{prefix}yours"><rect x="0" y="0" width="{256 - s}" height="512"/></clipPath>
    <clipPath id="{prefix}friends"><rect x="{256 + s}" y="0" width="{256 - s}" height="512"/></clipPath>
  </defs>
  <g transform="rotate({TILT} 256 256)">
    <g fill="none" stroke-width="{WIDTH}" stroke-linecap="round">
      <path d="M{l2[0]:.2f} {l2[1]:.2f} A{RADIUS} {RADIUS} 0 0 0 {l1[0]:.2f} {l1[1]:.2f}" stroke="{yours}"/>
      <path d="M{r1[0]:.2f} {r1[1]:.2f} A{RADIUS} {RADIUS} 0 0 1 {r2[0]:.2f} {r2[1]:.2f}" stroke="{friends}"/>
    </g>
    <circle cx="256" cy="256" r="{n}" fill="{node_yours}" clip-path="url(#{prefix}yours)"/>
    <circle cx="256" cy="256" r="{n}" fill="{node_friends}" clip-path="url(#{prefix}friends)"/>
  </g>'''


def icon(disc, yours, title):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512" role="img" aria-labelledby="title">
  <title id="title">{title}</title>
  <!-- Two arcs round a node: yours on the left, a friend's on the right, and the node between them
       split in two, each half the colour of the arc it faces. Drawn by build/logo/make-logo.py. -->
  <circle cx="256" cy="256" r="240" fill="{disc}"/>
  {mark(yours, TEAL)}
</svg>
'''


def mask():
    # Safari's pinned tab draws the shape in one colour of its choosing, so it is the mark alone.
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
  {mark('#000', '#000')}
</svg>
'''


FONT = "-apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans', Helvetica, Arial, sans-serif"


def banner(theme):
    t = {
        'light': dict(bg0='#FFFFFF', bg1='#E6F4F2', yours=DARK, faint=0.10, name='#0F172A', accent='#0D9488', note='#0F766E', border='#D0D7DE'),
        'dark': dict(bg0='#0D1117', bg1='#0B2A28', yours=LIGHT, faint=0.12, name='#F0F6FC', accent='#5EEAD4', note='#5EEAD4', border='#30363D'),
    }[theme]
    # The mark, large and faint behind the name, whole: 1.08 times, centred on the banner.
    k = 1.08
    tx, ty = 640 - 256 * k, 210 - 256 * k
    return f'''<svg width="1280" height="420" viewBox="0 0 1280 420" xmlns="http://www.w3.org/2000/svg" role="img" aria-labelledby="title">
  <title id="title">Nodearr: your friends' libraries, in your Arr apps</title>
  <!-- Drawn by build/logo/make-logo.py. -->
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1280" y2="420" gradientUnits="userSpaceOnUse">
      <stop offset="0%" stop-color="{t['bg0']}"/>
      <stop offset="100%" stop-color="{t['bg1']}"/>
    </linearGradient>
    <clipPath id="frame"><rect width="1280" height="420" rx="24"/></clipPath>
  </defs>

  <rect width="1280" height="420" rx="24" fill="url(#bg)"/>

  <g clip-path="url(#frame)">
    <g transform="translate({tx:g} {ty:g}) scale({k})" opacity="{t['faint']}">
      {mark(t['yours'], TEAL, prefix='banner-')}
    </g>
  </g>

  <rect x="0.5" y="0.5" width="1279" height="419" rx="23.5" fill="none" stroke="{t['border']}"/>

  <g font-family="{FONT}" text-anchor="middle">
    <text x="640" y="236" font-size="144" font-weight="700" letter-spacing="-4" fill="{t['name']}">Node<tspan fill="{t['accent']}">arr</tspan></text>
    <text x="640" y="300" font-size="30" font-weight="600" letter-spacing="1.5" fill="{t['note']}">YOUR FRIENDS' LIBRARIES, IN YOUR ARR APPS</text>
  </g>
</svg>
'''


def write(path, text):
    full = os.path.join(ROOT, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    print('wrote', path)


def social():
    """The picture GitHub shows for the repository on topic pages and in shared links, 1280 x 640.
    Dark, as it is mostly seen on dark pages: the mark, the name, and what Nodearr works with."""
    k = 0.62
    return f'''<svg width="1280" height="640" viewBox="0 0 1280 640" xmlns="http://www.w3.org/2000/svg" role="img" aria-labelledby="title">
  <title id="title">Nodearr: your friends' libraries, in your Arr apps</title>
  <!-- Drawn by build/logo/make-logo.py. Uploaded by hand: Settings > General > Social preview. -->
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1280" y2="640" gradientUnits="userSpaceOnUse">
      <stop offset="0%" stop-color="#0D1117"/>
      <stop offset="100%" stop-color="#0B2A28"/>
    </linearGradient>
  </defs>

  <rect width="1280" height="640" fill="url(#bg)"/>

  <!-- The mark, large and faint, off to the right, behind everything -->
  <g transform="translate(700 -60) scale(1.5)" opacity="0.06">
    {mark(LIGHT, TEAL, prefix='faint-')}
  </g>

  <!-- The icon, as it is everywhere else -->
  <g transform="translate(120 180) scale({k})">
    <circle cx="256" cy="256" r="240" fill="{DARK}"/>
    <circle cx="256" cy="256" r="236" fill="none" stroke="{TEAL}" stroke-opacity="0.35" stroke-width="8"/>
    {mark(LIGHT, TEAL, prefix='icon-')}
  </g>

  <g font-family="{FONT}">
    <text x="470" y="318" font-size="132" font-weight="700" letter-spacing="-4" fill="#F0F6FC">Node<tspan fill="#5EEAD4">arr</tspan></text>
    <text x="474" y="380" font-size="34" fill="#C9D1D9">Your friends' libraries, in your Arr apps.</text>
    <text x="474" y="436" font-size="24" font-weight="600" letter-spacing="1.5" fill="#5EEAD4">SONARR · RADARR · LIDARR · WHISPARR · PROWLARR</text>
  </g>
</svg>
'''


write('Logo/Nodearr.svg', icon(LIGHT, DARK, 'Nodearr'))
write('Logo/social-preview.svg', social())
write('Logo/Nodearr-dark.svg', icon(DARK, LIGHT, 'Nodearr'))
write('Logo/banner-light.svg', banner('light'))
write('Logo/banner-dark.svg', banner('dark'))
write('frontend/src/Content/Images/logo.svg', icon(LIGHT, DARK, 'Nodearr'))
write('frontend/src/Content/Images/Icons/safari-pinned-tab.svg', mask())
