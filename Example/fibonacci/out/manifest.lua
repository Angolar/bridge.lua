return function(dir)
    dir = (dir and #dir > 0) and (dir .. '.') or ""
    local require = require
    local load = function(module) return require(dir .. module) end

    load("Program")

    System.init{
        "Test.Program",
    }
end