// The zone's collision map (Maps/<zone>.map, the file the zone server reads: Zone/Include/map.h), for a
// bot to stay on the ground and not walk through what stands on it. Only the triangles are used; the
// file's own quadtree is skipped for a plain grid.
#ifndef BOTMAP_H
#define BOTMAP_H

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <algorithm>
#include <string>
#include <vector>

class BotMap
{
public:
	static const int kNoGround = -999999;

	bool Load(const std::string& file)
	{
		FILE* f = fopen(file.c_str(), "rb");
		if (!f)
			return false;
		unsigned char head[14];	// uint32 version, uint32 faces, uint16 nodes, uint32 face lists
		uint32_t version = 0, count = 0;
		bool ok = fread(head, sizeof(head), 1, f) == 1;
		if (ok)
		{
			memcpy(&version, head, 4);
			memcpy(&count, head + 4, 4);
			ok = version == 0x01000000 && count > 0 && count < 4000000;
		}
		if (ok)
		{
			faces.resize(count);	// 13 floats each: three corners, then the plane
			ok = fread(&faces[0], sizeof(Face), count, f) == count;
		}
		fclose(f);
		if (!ok)
		{
			faces.clear();
			return false;
		}
		minX = minY = 1e9f; float maxX = -1e9f, maxY = -1e9f;
		for (size_t i = 0; i < faces.size(); i++)
		{
			const Face& t = faces[i];
			minX = std::min(minX, std::min(t.ax, std::min(t.bx, t.cx))); maxX = std::max(maxX, std::max(t.ax, std::max(t.bx, t.cx)));
			minY = std::min(minY, std::min(t.ay, std::min(t.by, t.cy))); maxY = std::max(maxY, std::max(t.ay, std::max(t.by, t.cy)));
		}
		cols = (int)((maxX - minX) / kCell) + 1;
		rows = (int)((maxY - minY) / kCell) + 1;
		cells.assign((size_t)cols * rows, std::vector<uint32_t>());
		for (size_t i = 0; i < faces.size(); i++)
		{
			const Face& t = faces[i];
			int c0 = Col(std::min(t.ax, std::min(t.bx, t.cx))), c1 = Col(std::max(t.ax, std::max(t.bx, t.cx)));
			int r0 = Row(std::min(t.ay, std::min(t.by, t.cy))), r1 = Row(std::max(t.ay, std::max(t.by, t.cy)));
			for (int r = r0; r <= r1; r++)
				for (int c = c0; c <= c1; c++)
					cells[(size_t)r * cols + c].push_back((uint32_t)i);
		}
		return true;
	}

	bool Loaded() const { return !faces.empty(); }

	// The highest surface under (x, y) that is not above `from`: kNoGround when there is none.
	float Ground(float x, float y, float from) const
	{
		float best = kNoGround;
		if (!Inside(x, y))
			return best;
		const std::vector<uint32_t>& in = cells[(size_t)Row(y) * cols + Col(x)];
		for (size_t i = 0; i < in.size(); i++)
		{
			const Face& t = faces[in[i]];
			float d = (t.by - t.cy) * (t.ax - t.cx) + (t.cx - t.bx) * (t.ay - t.cy);
			if (fabsf(d) < 1e-6f)
				continue;	// seen edge on
			float u = ((t.by - t.cy) * (x - t.cx) + (t.cx - t.bx) * (y - t.cy)) / d;
			float v = ((t.cy - t.ay) * (x - t.cx) + (t.ax - t.cx) * (y - t.cy)) / d;
			if (u < 0 || v < 0 || u + v > 1)
				continue;
			float z = u * t.az + v * t.bz + (1 - u - v) * t.cz;
			if (z <= from && z > best)
				best = z;
		}
		return best;
	}

	// Does the segment a-b cross a triangle?
	bool Blocked(float ax, float ay, float az, float bx, float by, float bz) const
	{
		if (!Inside(ax, ay) || !Inside(bx, by))
			return false;
		int c0 = Col(std::min(ax, bx)), c1 = Col(std::max(ax, bx)), r0 = Row(std::min(ay, by)), r1 = Row(std::max(ay, by));
		for (int r = r0; r <= r1; r++)
			for (int c = c0; c <= c1; c++)
			{
				const std::vector<uint32_t>& in = cells[(size_t)r * cols + c];
				for (size_t i = 0; i < in.size(); i++)
					if (Crosses(faces[in[i]], ax, ay, az, bx, by, bz))
						return true;
			}
		return false;
	}

private:
	struct Face { float ax, ay, az, bx, by, bz, cx, cy, cz, nx, ny, nz, nd; };

	static const int kCell = 32;
	std::vector<Face> faces;
	std::vector<std::vector<uint32_t> > cells;
	float minX, minY;
	int cols, rows;

	int Col(float x) const { int c = (int)((x - minX) / kCell); return c < 0 ? 0 : c >= cols ? cols - 1 : c; }
	int Row(float y) const { int r = (int)((y - minY) / kCell); return r < 0 ? 0 : r >= rows ? rows - 1 : r; }
	bool Inside(float x, float y) const { return Loaded() && x >= minX && y >= minY && x < minX + cols * kCell && y < minY + rows * kCell; }

	// Moller-Trumbore, on the segment only.
	static bool Crosses(const Face& t, float ax, float ay, float az, float bx, float by, float bz)
	{
		float dx = bx - ax, dy = by - ay, dz = bz - az;
		float e1x = t.bx - t.ax, e1y = t.by - t.ay, e1z = t.bz - t.az, e2x = t.cx - t.ax, e2y = t.cy - t.ay, e2z = t.cz - t.az;
		float px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
		float det = e1x * px + e1y * py + e1z * pz;
		if (fabsf(det) < 1e-6f)
			return false;
		float sx = ax - t.ax, sy = ay - t.ay, sz = az - t.az;
		float u = (sx * px + sy * py + sz * pz) / det;
		if (u < 0 || u > 1)
			return false;
		float qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
		float v = (dx * qx + dy * qy + dz * qz) / det;
		if (v < 0 || u + v > 1)
			return false;
		float k = (e2x * qx + e2y * qy + e2z * qz) / det;
		return k >= 0 && k <= 1;
	}
};

#endif
