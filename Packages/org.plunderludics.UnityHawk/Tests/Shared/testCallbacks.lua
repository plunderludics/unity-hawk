
function main()
    print("running main");
    local called1 = false;
    local called2 = false;
    local result = "";
    while true do
        print("running");
        emu.yield() -- frameadvance() also works
        if emu.framecount() > 100 and not called1 then
            result = unityhawk.callmethod("reverseString", "Test");
            called1 = true;
        end
        if emu.framecount() > 110 and not called2 then
            unityhawk.callmethod("submitResult", result);
            called2 = true;
        end
    end
end

main()