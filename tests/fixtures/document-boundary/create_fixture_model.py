"""Generate the deterministic ONNX model used by boundary runtime tests."""

from pathlib import Path

import onnx
from onnx import TensorProto, helper


def create_model(output_path: Path) -> None:
    model_input = helper.make_tensor_value_info(
        "image", TensorProto.FLOAT, [1, 3, 320, 320])
    model_output = helper.make_tensor_value_info(
        "mask", TensorProto.FLOAT, [1, 1, 320, 320])
    axes = helper.make_tensor("axes", TensorProto.INT64, [1], [1])
    nodes = [
        helper.make_node("ReduceMean", ["image", "axes"], ["mean"], keepdims=1),
        helper.make_node("Sigmoid", ["mean"], ["mask"]),
    ]
    graph = helper.make_graph(
        nodes, "tiny-boundary-segmenter", [model_input], [model_output], [axes])
    model = helper.make_model(
        graph,
        producer_name="superscanner-test-fixture",
        opset_imports=[helper.make_opsetid("", 18)])
    model.ir_version = 10
    onnx.checker.check_model(model)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(model, output_path)


if __name__ == "__main__":
    create_model(Path(__file__).with_name("tiny-segmenter.onnx"))
