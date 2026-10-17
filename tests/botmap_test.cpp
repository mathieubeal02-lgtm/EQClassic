// Unit tests for tools/eqbot/BotMap.h (the bot's ground, walls, paths and steering) on a small map
// written here in the zone's .map format: a floor, and a pocket of three walls open away from the goal.
#include "../tools/eqbot/BotMap.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <vector>

static int failures = 0;
#define EXPECT(c) do { if (!(c)) { printf("FAIL line %d: %s\n", __LINE__, #c); failures++; } } while (0)

static std::vector<float> faces;

static void Tri(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
{
	float f[13] = { ax, ay, az, bx, by, bz, cx, cy, cz, 0, 0, 1, 0 };	// the plane is not read by BotMap
	faces.insert(faces.end(), f, f + 13);
}

// A wall from (x1, y1) to (x2, y2), 50 high
static void Wall(float x1, float y1, float x2, float y2)
{
	Tri(x1, y1, 0, x2, y2, 0, x2, y2, 50);
	Tri(x1, y1, 0, x2, y2, 50, x1, y1, 50);
}

int main()
{
	Tri(-200, -200, 0, 200, -200, 0, 200, 200, 0);
	Tri(-200, -200, 0, 200, 200, 0, -200, 200, 0);
	Wall(-20, -40, -20, 40);	// the pocket's back, between the start and the goal
	Wall(-80, 40, -20, 40);
	Wall(-80, -40, -20, -40);
	const char* file = "botmap_test.map";
	FILE* f = fopen(file, "wb");
	unsigned char head[14] = { 0, 0, 0, 1 };	// version 0x01000000, then the face count; no quadtree
	uint32_t n = (uint32_t)(faces.size() / 13);
	memcpy(head + 4, &n, 4);
	fwrite(head, sizeof(head), 1, f);
	fwrite(&faces[0], sizeof(float), faces.size(), f);
	fclose(f);

	BotMap map;
	EXPECT(map.Load(file));
	remove(file);
	EXPECT(map.Ground(0, 0, 10) == 0);
	EXPECT(map.Ground(0, 0, -1) == BotMap::kNoGround);
	EXPECT(map.Ground(500, 0, 10) == BotMap::kNoGround);
	EXPECT(map.Blocked(-30, 0, 4, 0, 0, 4));
	EXPECT(!map.Blocked(-30, 0, 60, 0, 0, 60));	// over the wall
	float z;
	EXPECT(!map.Walkable(-30, 0, 0, 60, 0, 8, 4, z));
	EXPECT(map.Walkable(-30, 0, 0, -60, 0, 8, 4, z) && z == 0);

	// a path out of the pocket and round it, every leg walkable
	std::vector<std::pair<float, float> > path = map.Path(-30, 0, 0, 60, 0, 8, 4, 8000);
	EXPECT(path.size() >= 2);
	if (!path.empty())
	{
		EXPECT(path.back().first == 60 && path.back().second == 0);
		float x = -30, y = 0, h = 0, length = 0;
		for (size_t i = 0; i < path.size(); i++)
		{
			EXPECT(map.Walkable(x, y, h, path[i].first, path[i].second, 8, 4, h));
			length += BotMap::Dist(x, y, path[i].first, path[i].second);
			x = path[i].first; y = path[i].second;
		}
		EXPECT(length < 260);	// out past the pocket's end (x < -80), round its corner and on: about 230
	}
	// a goal off the map: none (the search stops at its budget)
	EXPECT(map.Path(-30, 0, 0, 500, 0, 8, 4, 2000).empty());

	// the walker gets there without going through
	BotWalker walker;
	float x = -30, y = 0, feet = 0;
	int through = 0, steps = 0;
	for (; steps < 200 && BotMap::Dist(x, y, 60, 0) > 1; steps++)
		through += walker.Walk(map, x, y, feet, 60, 0, 7.5f, steps * 250L).through;
	EXPECT(BotMap::Dist(x, y, 60, 0) <= 1);
	EXPECT(through == 0);
	EXPECT(feet == 0);

	if (failures)
		printf("%d failure(s)\n", failures);
	else
		printf("botmap: all passed\n");
	return failures ? 1 : 0;
}
