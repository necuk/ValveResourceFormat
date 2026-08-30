namespace ValveResourceFormat.Particles.Utils
{
    static class VectorExpressionEvaluator
    {
        private const float DivisorFloor = 1.1920929e-7f;

        private const float SmallestNormal = 1.17549435e-38f;

        public static Vector3 Evaluate(VectorExpression expression, Vector3 input1, Vector3 input2, float lerp)
            => expression switch
            {
                VectorExpression.VECTOR_EXPRESSION_UNINITIALIZED => Vector3.Zero,
                VectorExpression.VECTOR_EXPRESSION_ADD => input1 + input2,
                VectorExpression.VECTOR_EXPRESSION_SUBTRACT => input1 - input2,
                VectorExpression.VECTOR_EXPRESSION_MUL => input1 * input2,
                VectorExpression.VECTOR_EXPRESSION_DIVIDE => input1 / FloorDivisor(input2),
                VectorExpression.VECTOR_EXPRESSION_INPUT_1 => input1,
                VectorExpression.VECTOR_EXPRESSION_MIN => Vector3.Min(input1, input2),
                VectorExpression.VECTOR_EXPRESSION_MAX => Vector3.Max(input1, input2),
                VectorExpression.VECTOR_EXPRESSION_CROSSPRODUCT => Vector3.Cross(input1, input2),
                VectorExpression.VECTOR_EXPRESSION_LERP => Vector3.Lerp(input1, input2, MathUtils.Saturate(lerp)),
                _ => throw new NotImplementedException($"Unrecognized vector expression type ({expression})")
            };

        private static Vector3 FloorDivisor(Vector3 divisor) => new(
            FloorDivisor(divisor.X),
            FloorDivisor(divisor.Y),
            FloorDivisor(divisor.Z));

        private static float FloorDivisor(float divisor)
            => MathF.Abs(divisor) < SmallestNormal
                ? MathF.CopySign(DivisorFloor, divisor)
                : divisor;
    }
}
