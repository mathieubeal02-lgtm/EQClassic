// The zone's collision map (Maps/<zone>.map, the file the zone server reads: Zone/Include/map.h), for a
// bot to stay on the ground and not walk through what stands on it. Only the triangles are used; the
// file's own quadtree is skipped for a plain grid.
#ifndef BOTMAP_H
#define BOTMAP_H

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <time.h>
#include <algorithm>
#include <map>
#include <set>
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

	// The feet of someone at z, `above` units over the ground: the surface right under, unless there is
	// none near (a hole in the map, water the map lacks): then z - above as it is.
	float Feet(float x, float y, float z, float above) const
	{
		float g = Ground(x, y, z + 2);
		return g == kNoGround || g < z - above - kMaxDrop ? z - above : g;
	}

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

	// Can a walker go straight from a (feet at az) to b: ground all along, never higher than stepUp above
	// the last, nothing crossed at eye height above the feet? Gives the feet's height at b.
	bool Walkable(float ax, float ay, float az, float bx, float by, float stepUp, float eye, float& bz) const
	{
		float dx = bx - ax, dy = by - ay, len = sqrtf(dx * dx + dy * dy);
		int n = (int)(len / kGrid) + 1;
		float x = ax, y = ay, z = az;
		for (int i = 1; i <= n; i++)
		{
			float nx = ax + dx * i / n, ny = ay + dy * i / n, g = Ground(nx, ny, z + stepUp);
			if (g == kNoGround || g < z - kMaxDrop || Blocked(x, y, z + eye, nx, ny, g + eye))
				return false;
			x = nx; y = ny; z = g;
		}
		bz = z;
		return true;
	}

	// A way round what stands between a and b (A* on a grid of kGrid units, from a's feet): the points to
	// go through, b last, with straight runs merged. Empty when none turned up within `budget` cells.
	std::vector<std::pair<float, float> > Path(float ax, float ay, float az, float bx, float by, float stepUp, float eye, int budget) const
	{
		struct Node { float g, f, z; int parent; int i, j; };
		std::vector<Node> nodes;
		std::map<long, int> seen;	// cell -> node
		std::set<std::pair<float, int> > open;	// f, node
		std::vector<std::pair<float, float> > out;
		if (!Loaded())
			return out;
		const float goal = kGrid * 1.5f;
		nodes.push_back(Node{ 0, Dist(ax, ay, bx, by), az, -1, 0, 0 });
		seen[Key(0, 0)] = 0;
		open.insert(std::make_pair(nodes[0].f, 0));
		int found = -1;
		while (!open.empty() && (int)nodes.size() < budget)
		{
			int cur = open.begin()->second;
			open.erase(open.begin());
			Node c = nodes[cur];
			float cx = ax + c.i * kGrid, cy = ay + c.j * kGrid;
			if (Dist(cx, cy, bx, by) <= goal)
			{
				float bz;
				if (Walkable(cx, cy, c.z, bx, by, stepUp, eye, bz)) { found = cur; break; }
			}
			for (int d = 0; d < 8; d++)
			{
				static const int di[] = { 1, 1, 0, -1, -1, -1, 0, 1 }, dj[] = { 0, 1, 1, 1, 0, -1, -1, -1 };
				int ni = c.i + di[d], nj = c.j + dj[d];
				float nx = ax + ni * kGrid, ny = ay + nj * kGrid, step = d & 1 ? kGrid * 1.4142f : kGrid;
				std::map<long, int>::iterator s = seen.find(Key(ni, nj));
				if (s != seen.end() && nodes[s->second].g <= c.g + step)
					continue;
				float g = Ground(nx, ny, c.z + stepUp);
				if (g == kNoGround || g < c.z - kMaxDrop || Blocked(cx, cy, c.z + eye, nx, ny, g + eye))
					continue;
				Node n = { c.g + step, c.g + step + Dist(nx, ny, bx, by), g, cur, ni, nj };
				if (s != seen.end())
				{
					open.erase(std::make_pair(nodes[s->second].f, s->second));
					nodes[s->second] = n;
					open.insert(std::make_pair(n.f, s->second));
				}
				else
				{
					seen[Key(ni, nj)] = (int)nodes.size();
					nodes.push_back(n);
					open.insert(std::make_pair(n.f, (int)nodes.size() - 1));
				}
			}
		}
		if (found < 0)
			return out;
		std::vector<int> chain;
		for (int k = found; k >= 0; k = nodes[k].parent)
			chain.push_back(k);
		std::reverse(chain.begin(), chain.end());
		// from each point, the farthest one further on that a straight walk reaches (else the next cell)
		float x = ax, y = ay, z = az;
		for (size_t at = 0; at + 1 < chain.size(); )
		{
			size_t next = at + 1;
			float nz = nodes[chain[next]].z;
			for (size_t k = chain.size() - 1; k > at + 1; k--)
			{
				float pz;
				if (Walkable(x, y, z, ax + nodes[chain[k]].i * kGrid, ay + nodes[chain[k]].j * kGrid, stepUp, eye, pz)) { next = k; nz = pz; break; }
			}
			at = next;
			x = ax + nodes[chain[at]].i * kGrid; y = ay + nodes[chain[at]].j * kGrid; z = nz;
			out.push_back(std::make_pair(x, y));
		}
		out.push_back(std::make_pair(bx, by));
		return out;
	}

	static const int kGrid = 8;	// the path grid: narrower than the gaps between trunks
	// A drop no walker takes: through a hole in the map it would land on the zone's boundary floor,
	// 60 units under Innothule's swamp, and walk on under the world
	static const int kMaxDrop = 30;

private:
	struct Face { float ax, ay, az, bx, by, bz, cx, cy, cz, nx, ny, nz, nd; };

	static long Key(int i, int j) { return (long)i * 100003L + j; }
public:
	static float Dist(float ax, float ay, float bx, float by) { return sqrtf((bx - ax) * (bx - ax) + (by - ay) * (by - ay)); }
private:

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

// A walker's steering on a BotMap: straight at the goal; if something is in the way, the smallest free turn,
// on the side taken a moment ago (else it sways left and right before a wall); turned aside or shut in, a
// way round (BotMap::Path) followed point by point. Shut in with no way round, it walks through.
class BotWalker
{
public:
	struct Step
	{
		bool moved, through;	// a step was taken; it went through something
		int turn;				// degrees turned aside (0: straight)
		bool newDetour;			// that turn starts a detour (none in the last 2 s)
		int search;				// a path was looked for: 1 found, -1 none
		int points; long ms;	// the path's points, the search's time
		float headX, headY;		// where to face
	};
	bool paths;	// look for paths (else turns only)

	BotWalker() : paths(true), side(1), sideUntil(0), at(0), aside(0), searchAfter(0), failX(0), failY(0) {}

	void Reset() { path.clear(); aside = 0; }

	// Up to s units from (x, y), feet at `feet`, toward (tx, ty), at `now` (ms); x, y and feet move.
	Step Walk(const BotMap& map, float& x, float& y, float& feet, float tx, float ty, float s, long now)
	{
		Step r = { false, false, 0, false, 0, 0, 0, tx, ty };
		s = std::min(s, BotMap::Dist(x, y, tx, ty));	// never past the goal
		// a path made for somewhere else (the target moved on): dropped
		if (!path.empty() && (fabsf(path.back().first - tx) > 20 || fabsf(path.back().second - ty) > 20))
			path.clear();
		// the next point of the path, walked right onto: the way on from it is clear from there, not from a
		// step aside (passing a step short of the points walked into the corners they go round)
		float ax = tx, ay = ty;
		while (!path.empty() && at + 1 < path.size() && BotMap::Dist(x, y, path[at].first, path[at].second) < 0.5f)
			at++;
		if (!path.empty() && at + 1 < path.size())
		{
			ax = path[at].first; ay = path[at].second;
			s = std::min(s, BotMap::Dist(x, y, ax, ay));
		}
		// turning aside for 3 s while on a path: it no longer fits (something moved us): a new one
		if (!path.empty() && aside > 12 && now >= searchAfter)
		{
			aside = 0;
			if (Search(map, x, y, feet, tx, ty, now, r))
				return r;
		}
		static const float kTurn[] = { 0, 30, 60, 90, 120 };
		float base = atan2f(ay - y, ax - x);
		int first = now < sideUntil ? side : 1;
		for (int k = 0; k < 5; k++)
			for (int t = 0; t < (k ? 2 : 1); t++)
			{
				int sd = t ? -first : first;
				float a = base + sd * kTurn[k] * 3.14159265f / 180;
				float nx = x + cosf(a) * s, ny = y + sinf(a) * s;
				float g = map.Ground(nx, ny, feet + kStepUp);
				if (g == BotMap::kNoGround || g < feet - BotMap::kMaxDrop || map.Blocked(x, y, feet + kEye, nx, ny, g + kEye))
					continue;
				if (k > 0)
				{
					r.turn = (int)(sd * kTurn[k]);
					r.newDetour = now >= sideUntil;
					side = sd; sideUntil = now + 2000;
					// turning aside without a path: look for one (a turn alone walks into dead ends)
					if (path.empty() && now >= searchAfter && Search(map, x, y, feet, tx, ty, now, r))
						return r;
				}
				aside = k > 0 ? aside + 1 : 0;
				x = nx; y = ny; feet = g;
				r.moved = true;
				r.headX = x + cosf(a) * 10; r.headY = y + sinf(a) * 10;
				return r;
			}
		// shut in on all sides: a path from here, else through
		if (now >= searchAfter && Search(map, x, y, feet, tx, ty, now, r))
			return r;
		float d = BotMap::Dist(x, y, tx, ty);
		if (d > 0.01f) { x += (tx - x) / d * s; y += (ty - y) / d * s; }
		float g = map.Ground(x, y, feet + 50);
		if (g != BotMap::kNoGround && g >= feet - BotMap::kMaxDrop) feet = g;
		r.moved = r.through = true;
		return r;
	}

	static const int kStepUp = 8;	// what we step up without a jump
	static const int kEye = 4;		// the height above the feet that must be free

private:
	std::vector<std::pair<float, float> > path;
	int side; long sideUntil;
	size_t at;
	int aside;	// steps in a row turned aside
	long searchAfter;
	float failX, failY;	// the last place no path led to

	bool Search(const BotMap& map, float x, float y, float feet, float tx, float ty, long now, Step& r)
	{
		searchAfter = now + 3000;	// at most one search every 3 s: each costs up to ~70 ms (~200 far away)
		float d = BotMap::Dist(x, y, tx, ty);
		if (!paths || d > 3000)
			return false;
		clock_t c0 = clock();
		// A* heads for the goal: across open ground a far one costs little more, a maze much more
		path = map.Path(x, y, feet, tx, ty, kStepUp, kEye, d > 600 ? 25000 : 8000);
		at = 0;
		r.ms = (long)((clock() - c0) * 1000 / CLOCKS_PER_SEC);
		r.points = (int)path.size();
		r.search = path.empty() ? -1 : 1;
		if (path.empty())
		{
			// up a slope too steep, or inside something: not again for that place before 20 s
			if (fabsf(tx - failX) < 20 && fabsf(ty - failY) < 20)
				searchAfter = now + 20000;
			failX = tx; failY = ty;
		}
		return !path.empty();
	}
};

#endif
