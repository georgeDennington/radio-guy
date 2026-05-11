--[[ radio-man — DCS Export script

Streams the player's ownship + weather state over UDP to localhost:49152,
once per second, as JSON. The C# DcsExportClient on the radio-man side
listens, parses, and feeds the Tower agent with real game data.

INSTALL
-------
Copy or symlink this file into your DCS scripts folder:

  %USERPROFILE%\Saved Games\DCS\Scripts\Export.lua

  (or DCS.openbeta\Scripts\, depending on your DCS version)

If you already have an Export.lua there, *append* this file's contents to
the bottom of yours — we chain LuaExportAfterNextFrame so we don't clobber
anyone else's exports.

NOTES
-----
- Tested for fixed-wing DCS modules. Helo / civilian aircraft may need
  different data-extraction calls.
- The "gearDown" field is a placeholder (always false) since DCS doesn't
  expose a unified gear state across modules. Wire up per-module if needed.
- Coordinates: DCS's local X/Z map to north/east on most maps. If lat/lon
  comes out wrong, try swapping LoLoCoordinatesToGeoCoordinates arguments.
]]--

local socket = require("socket")
local senderUdp = socket.udp()
senderUdp:setpeername("127.0.0.1", 49152)

local lastSendTime = 0
local sendIntervalSec = 1.0

local prevLuaExportAfterNextFrame = LuaExportAfterNextFrame
function LuaExportAfterNextFrame()
    if prevLuaExportAfterNextFrame then prevLuaExportAfterNextFrame() end

    local now = LoGetModelTime()
    if now - lastSendTime < sendIntervalSec then return end
    lastSendTime = now

    local self = LoGetSelfData()
    if not self or not self.Position then return end

    -- DCS local Cartesian → geographic (lat, lon in degrees)
    local lat, lon = LoLoCoordinatesToGeoCoordinates(self.Position.x, self.Position.z)

    -- Altitude MSL: Position.y is metres above sea level
    local altFt = self.Position.y * 3.28084

    -- True heading: self.Heading is radians, 0 = north, positive clockwise (or
    -- counterclockwise depending on convention — swap sign if it comes out wrong).
    local hdgDeg = math.deg(self.Heading or 0)
    if hdgDeg < 0 then hdgDeg = hdgDeg + 360 end

    -- IAS in knots (LoGetIndicatedAirSpeed returns m/s)
    local ias = LoGetIndicatedAirSpeed() or 0
    local iasKts = ias * 1.94384

    -- Wind at current altitude. LoGetVectorWindVelocity returns components
    -- in the world frame (m/s). We compute direction-FROM in degrees true.
    local wind = LoGetVectorWindVelocity(self.Position.y) or { x = 0, y = 0, z = 0 }
    local windDirTo = math.deg(math.atan2(wind.x, wind.z))
    if windDirTo < 0 then windDirTo = windDirTo + 360 end
    local windFrom = (windDirTo + 180) % 360
    local windMag = math.sqrt(wind.x * wind.x + wind.z * wind.z)
    local windKts = windMag * 1.94384

    local callsign = self.UnitName or "Player"

    -- Build JSON manually (avoid pulling in a json library)
    local json = string.format(
        '{"callsign":"%s","lat":%.6f,"lon":%.6f,"altFt":%.0f,"heading":%.0f,"speedKts":%.0f,"gearDown":false,"windFromTrue":%d,"windKts":%d}',
        callsign,
        lat or 0, lon or 0,
        altFt, hdgDeg, iasKts,
        math.floor(windFrom + 0.5),
        math.floor(windKts + 0.5)
    )

    senderUdp:send(json)
end
