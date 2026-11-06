#!/usr/bin/env bash
# Encodes the rendered master into the three files the website serves.
#   usage: bash encode.sh [mp4 crf] [webm crf]
# master: renders/master.mp4 (npx hyperframes render -o renders/master.mp4 --crf 8)
set -euo pipefail
cd "$(dirname "$0")"
MASTER=renders/master.mp4
OUT=../../server/static/media
CRF_MP4=${1:-22}
CRF_WEBM=${2:-36}
mkdir -p "$OUT"

# H.264, yuv420p, faststart, no audio. A keyframe every 2 s keeps seeking cheap for the loop.
ffmpeg -v error -y -i "$MASTER" -an -c:v libx264 -preset slow -tune animation -crf "$CRF_MP4" \
  -pix_fmt yuv420p -profile:v high -g 60 -movflags +faststart "$OUT/hero.mp4"

# VP9, no audio, constant quality, two passes.
ffmpeg -v error -y -i "$MASTER" -an -c:v libvpx-vp9 -b:v 0 -crf "$CRF_WEBM" -row-mt 1 -tile-columns 2 \
  -g 60 -pix_fmt yuv420p -pass 1 -passlogfile renders/vp9 -f null NUL
ffmpeg -v error -y -i "$MASTER" -an -c:v libvpx-vp9 -b:v 0 -crf "$CRF_WEBM" -row-mt 1 -tile-columns 2 \
  -g 60 -pix_fmt yuv420p -pass 2 -passlogfile renders/vp9 "$OUT/hero.webm"

# The poster is frame 0.
ffmpeg -v error -y -i "$MASTER" -frames:v 1 -q:v 3 "$OUT/hero-poster.jpg"

ls -l "$OUT"/hero.mp4 "$OUT"/hero.webm "$OUT"/hero-poster.jpg
