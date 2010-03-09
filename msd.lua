function Init()
    indicator:name("Moving Standard Deviation");
    indicator:description("Moving Standard Deviation");
    indicator:requiredSource(core.Tick);
    indicator:type(core.Oscillator);

    indicator.parameters:addInteger("N", "Number of periods of Standard Deviation", "", 20, 2, 300);
    indicator.parameters:addColor("clrMva", "Color of Standard Deviation", "", core.rgb(255, 0, 0));
end

local first = 0;        -- first period we can calculate
local n = 0;            -- MVA parameter
local source = nil;     -- source
local mva = nil;        -- moving average

-- initializes the instance of the indicator
function Prepare()
    source = instance.source;
    n = instance.parameters.N;

    first = n + source:first() - 1;
    local name = profile:id() .. "(" .. source:name() .. "," .. n .. ")";
    instance:name(name);

    mva = instance:addStream("MSD", core.Line, name .. ".MSD", "MSD", instance.parameters.clrMva,  first)
end

-- calculate the value
function Update(period)
    if (period >= first) then
        mva[period] = core.stdev(source, core.rangeTo(period, n));
    end
end

