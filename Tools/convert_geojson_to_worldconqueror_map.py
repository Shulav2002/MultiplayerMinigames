#!/usr/bin/env python3
"""
Convert GeoJSON state boundaries into a simplified JSON format for WorldConqueror.

Example:
python Tools/convert_geojson_to_worldconqueror_map.py ^
  --input usa_states.geojson ^
  --output Assets/games/WorldConqueror/Data/usa_states_simplified.json ^
  --name-property NAME ^
  --id-property STUSPS ^
  --exclude AK HI PR GU VI MP AS ^
  --target-width 18 ^
  --target-height 12 ^
  --tolerance 0.08
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Iterable


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Convert GeoJSON into simplified WorldConqueror map JSON.")
    parser.add_argument("--input", required=True, help="Path to a GeoJSON FeatureCollection.")
    parser.add_argument("--output", required=True, help="Output JSON path.")
    parser.add_argument("--name-property", default="NAME", help="GeoJSON property used for displayName.")
    parser.add_argument("--id-property", default="STUSPS", help="GeoJSON property used for id.")
    parser.add_argument("--exclude", nargs="*", default=[], help="State ids to exclude.")
    parser.add_argument("--target-width", type=float, default=18.0, help="Final map width in Unity units.")
    parser.add_argument("--target-height", type=float, default=12.0, help="Final map height in Unity units.")
    parser.add_argument("--tolerance", type=float, default=0.08, help="RDP simplification tolerance in source units.")
    parser.add_argument("--min-points", type=int, default=6, help="Minimum points to keep per state polygon.")
    parser.add_argument("--keep-territories", action="store_true", help="Keep multipolygon islands instead of only largest landmass.")
    return parser.parse_args()


def load_geojson(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def sq_distance(a: tuple[float, float], b: tuple[float, float]) -> float:
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    return dx * dx + dy * dy


def sq_segment_distance(point: tuple[float, float], start: tuple[float, float], end: tuple[float, float]) -> float:
    x = start[0]
    y = start[1]
    dx = end[0] - x
    dy = end[1] - y

    if dx != 0.0 or dy != 0.0:
        t = ((point[0] - x) * dx + (point[1] - y) * dy) / (dx * dx + dy * dy)
        if t > 1.0:
            x = end[0]
            y = end[1]
        elif t > 0.0:
            x += dx * t
            y += dy * t

    dx = point[0] - x
    dy = point[1] - y
    return dx * dx + dy * dy


def rdp(points: list[tuple[float, float]], tolerance: float) -> list[tuple[float, float]]:
    if len(points) < 3:
        return points[:]

    max_distance = 0.0
    index = 0
    last = len(points) - 1

    for i in range(1, last):
        distance = sq_segment_distance(points[i], points[0], points[last])
        if distance > max_distance:
            index = i
            max_distance = distance

    if max_distance > tolerance * tolerance:
        left = rdp(points[: index + 1], tolerance)
        right = rdp(points[index:], tolerance)
        return left[:-1] + right

    return [points[0], points[last]]


def polygon_area(points: Iterable[tuple[float, float]]) -> float:
    points = list(points)
    area = 0.0
    for index, current in enumerate(points):
        next_point = points[(index + 1) % len(points)]
        area += current[0] * next_point[1] - next_point[0] * current[1]
    return abs(area) * 0.5


def close_ring(points: list[tuple[float, float]]) -> list[tuple[float, float]]:
    if not points:
        return points
    if points[0] == points[-1]:
        return points[:-1]
    return points


def ensure_minimum_points(points: list[tuple[float, float]], minimum: int) -> list[tuple[float, float]]:
    if len(points) >= minimum:
        return points
    return points


def pick_polygon_coordinates(geometry: dict, keep_territories: bool) -> list[tuple[float, float]]:
    geometry_type = geometry.get("type")
    coordinates = geometry.get("coordinates", [])

    if geometry_type == "Polygon":
        return close_ring([(point[0], point[1]) for point in coordinates[0]])

    if geometry_type != "MultiPolygon":
        return []

    polygons = []
    for polygon in coordinates:
        outer_ring = close_ring([(point[0], point[1]) for point in polygon[0]])
        if outer_ring:
            polygons.append(outer_ring)

    if not polygons:
        return []

    if keep_territories:
        merged: list[tuple[float, float]] = []
        for polygon in polygons:
            merged.extend(polygon)
        return merged

    return max(polygons, key=polygon_area)


def bounds_of_all(polygons: list[list[tuple[float, float]]]) -> tuple[float, float, float, float]:
    min_x = math.inf
    min_y = math.inf
    max_x = -math.inf
    max_y = -math.inf

    for polygon in polygons:
        for x, y in polygon:
            min_x = min(min_x, x)
            min_y = min(min_y, y)
            max_x = max(max_x, x)
            max_y = max(max_y, y)

    return min_x, min_y, max_x, max_y


def centroid(points: list[tuple[float, float]]) -> tuple[float, float]:
    signed_area = 0.0
    centroid_x = 0.0
    centroid_y = 0.0

    for index, current in enumerate(points):
        next_point = points[(index + 1) % len(points)]
        cross = current[0] * next_point[1] - next_point[0] * current[1]
        signed_area += cross
        centroid_x += (current[0] + next_point[0]) * cross
        centroid_y += (current[1] + next_point[1]) * cross

    if abs(signed_area) < 1e-6:
        avg_x = sum(point[0] for point in points) / len(points)
        avg_y = sum(point[1] for point in points) / len(points)
        return avg_x, avg_y

    scale = 1.0 / (3.0 * signed_area)
    return centroid_x * scale, centroid_y * scale


def normalize_point(
    point: tuple[float, float],
    min_x: float,
    min_y: float,
    scale: float,
    target_width: float,
    target_height: float,
) -> tuple[float, float]:
    normalized_x = (point[0] - min_x) * scale - target_width * 0.5
    normalized_y = target_height * 0.5 - (point[1] - min_y) * scale
    return normalized_x, normalized_y


def main() -> None:
    args = parse_args()
    geojson = load_geojson(Path(args.input))
    features = geojson.get("features", [])
    excluded = {value.upper() for value in args.exclude}

    raw_regions = []
    polygons = []

    for feature in features:
        properties = feature.get("properties", {})
        state_id = str(properties.get(args.id_property, properties.get(args.name_property, ""))).strip()
        display_name = str(properties.get(args.name_property, state_id)).strip()
        if not state_id or state_id.upper() in excluded:
            continue

        polygon = pick_polygon_coordinates(feature.get("geometry", {}), args.keep_territories)
        if len(polygon) < 3:
            continue

        simplified = rdp(polygon, args.tolerance)
        simplified = ensure_minimum_points(simplified, args.min_points)
        if len(simplified) < 3:
            continue

        polygons.append(simplified)
        raw_regions.append(
            {
                "id": state_id,
                "displayName": display_name,
                "points": simplified,
            }
        )

    if not raw_regions:
        raise SystemExit("No valid regions found in the input file.")

    min_x, min_y, max_x, max_y = bounds_of_all(polygons)
    source_width = max(max_x - min_x, 1e-6)
    source_height = max(max_y - min_y, 1e-6)
    scale = min(args.target_width / source_width, args.target_height / source_height)

    result = {"regions": []}
    for region in raw_regions:
        normalized_points = [
            normalize_point(point, min_x, min_y, scale, args.target_width, args.target_height)
            for point in region["points"]
        ]
        label_x, label_y = centroid(normalized_points)
        result["regions"].append(
            {
                "id": region["id"],
                "displayName": region["displayName"],
                "hasLabelPosition": True,
                "labelPosition": {"x": round(label_x, 4), "y": round(label_y, 4)},
                "points": [{"x": round(x, 4), "y": round(y, 4)} for x, y in normalized_points],
            }
        )

    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8") as handle:
        json.dump(result, handle, ensure_ascii=True, indent=2)


if __name__ == "__main__":
    main()
