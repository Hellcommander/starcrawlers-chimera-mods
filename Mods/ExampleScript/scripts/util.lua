-- A helper module: require("util") from any script in this folder returns this table.
-- Use, copy and change it for any purpose, with no conditions and no credit needed.
local M = {}

function M.round(x) return math.floor(x + 0.5) end

-- Counts the entries of a table with string keys (# only counts lists).
function M.count(t)
  local n = 0
  for _ in pairs(t) do n = n + 1 end
  return n
end

return M
