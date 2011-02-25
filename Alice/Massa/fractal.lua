function Init()
    indicator:name("Fractal");
    indicator:description("Bill Williams Fractal oscillator")
    indicator:requiredSource(core.Bar);
    indicator:type(core.Oscillator);

    indicator.parameters:addColor("UpC", "Color of the up fractal", "", core.rgb(255, 0, 0));
    indicator.parameters:addColor("DownC", "Color of the down fractal", "", core.rgb(0, 255, 0));
end

local source;
local up, down;

function Prepare()
    source = instance.source;
    local name = profile:id();
    instance:name(name);
    up = instance:addStream("Up", core.Bar, name .. ".Up", "Up", instance.parameters.UpC, 4, -2);
    up:addLevel(1);
    up:addLevel(0);
    up:addLevel(-1);
    down = instance:addStream("Down", core.Bar, name .. ".Down", "Down", instance.parameters.DownC, 4, -2);
end

function Update(period, mode)
    if (period > 6) then
        local curr = source.high[period - 2];
        if (curr > source.high[period - 4] and curr > source.high[period - 3] and
            curr > source.high[period - 1] and curr > source.high[period]) then
            up[period - 2] = 1;
        else
            up[period - 2] = nil;
        end
        curr = source.low[period - 2];
        if (curr < source.low[period - 4] and curr < source.low[period - 3] and
            curr < source.low[period - 1] and curr < source.low[period]) then
            down[period - 2] = -1;
        else
            down[period - 2] = nil;
        end
    end
end
