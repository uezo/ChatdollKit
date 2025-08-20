mergeInto(LibraryManager.library, {
    GetCameraDevices: function(targetObjectNamePtr, targetFunctionNamePtr) {
        let targetObjectName = UTF8ToString(targetObjectNamePtr);
        let targetFunctionName = UTF8ToString(targetFunctionNamePtr);

        async function getCameraNames() {
            try {
                const stream = await navigator.mediaDevices.getUserMedia({ video: true });
                stream.getTracks().forEach(track => track.stop());
            
                const devices = await navigator.mediaDevices.enumerateDevices();
                const cameras = devices.filter(device => device.kind === "videoinput");
                const cameraNames = [];
                for (const camera of cameras) {
                    if (camera.label) {
                        cameraNames.push(camera.label);
                    }
                }
                SendMessage(targetObjectName, targetFunctionName, JSON.stringify({names: cameraNames}));
            
            } catch (error) {
                console.error("Error at GetCameraDevices", error);
                return [];
            }
        }

        getCameraNames();
    }
});
