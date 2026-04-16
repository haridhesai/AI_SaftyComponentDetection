from flask import Flask, request, jsonify
import cv2
import numpy as np
from ultralytics import YOLO

app = Flask(__name__)

model = YOLO("best.pt")  # change to your model path

CLASS_ESD_NOT_WEARING = 0
CLASS_ESD_WEARING = 2

@app.route('/detect', methods=['POST'])
def detect():
    file = request.files['image']
    img_bytes = file.read()

    np_arr = np.frombuffer(img_bytes, np.uint8)
    frame = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)

    results = model.predict(frame, conf=0.55)

    wearing = 0
    not_wearing = 0

    if results and results[0].boxes is not None:
        for box in results[0].boxes:
            cls = int(box.cls[0])

            if cls == CLASS_ESD_WEARING:
                wearing += 1
            elif cls == CLASS_ESD_NOT_WEARING:
                not_wearing += 1

    return jsonify({
        "wearing": wearing,
        "not_wearing": not_wearing
    })

if __name__ == '__main__':
    app.run(host="127.0.0.1", port=5000)