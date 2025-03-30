using System.Runtime.InteropServices.WindowsRuntime;
using Windows.AI.MachineLearning;
using Windows.Storage.Streams;
using WinRT;

namespace PaddleOnWindowsML.Paddle
{
    public enum PaddleDevice
    {
        Cpu,
        Gpu
    }

    public class PaddlePredictor : IDisposable
    {
        private bool disposeValue;

        private LearningModel model = null!;
        private IReadOnlyList<long> inputShapes;

        public PaddlePredictor(IRandomAccessStreamReference stream)
            : this(LearningModel.LoadFromStream(stream)) { }

        private PaddlePredictor(LearningModel model)
        {
            this.model = model;
            inputShapes = model.InputFeatures[0].As<TensorFeatureDescriptor>().Shape.ToArray();
        }

        public IReadOnlyList<long> InputShapes => inputShapes;

        public PaddleSession CreateSession(PaddleDevice device)
        {
            var device2 = device switch
            {
                PaddleDevice.Gpu => LearningModelDeviceKind.DirectXHighPerformance,
                _ => LearningModelDeviceKind.Cpu,
            };
            var session = new LearningModelSession(model, new LearningModelDevice(device2));
            session.EvaluationProperties["session.disable_cpu_ep_fallback"] = true;
            return new PaddleSession(session);
        }

        public void Dispose()
        {
            if (!disposeValue)
            {
                model?.Dispose();
                model = null!;

                GC.SuppressFinalize(this);

                disposeValue = true;
            }
        }

        public static async Task<PaddlePredictor> CreateAsync(IRandomAccessStreamReference stream)
        {
            var model = await LearningModel.LoadFromStreamAsync(stream);
            return new PaddlePredictor(model);
        }

    }

    public class PaddleSession : IDisposable
    {
        private bool disposeValue;
        private LearningModelSession session;
        private LearningModelBinding binding;

        public PaddleSession(LearningModelSession session)
        {
            this.session = session;
            this.binding = new LearningModelBinding(session);
        }

        public async Task<PaddleDetectOutput> RunAsync(PaddleDetectInput input, CancellationToken cancellationToken)
        {
            binding.Bind(session.Model.InputFeatures[0].Name, input.images);
            var result = await session.EvaluateAsync(binding, "0").AsTask(cancellationToken);
            var output = new PaddleDetectOutput();
            output.outputs = result.Outputs[session.Model.OutputFeatures[0].Name] as TensorFloat;
            return output;
        }

        public void Dispose()
        {
            if (!disposeValue)
            {
                session?.Dispose();
                session = null!;
                binding = null!;

                disposeValue = true;
            }
        }
    }

    public class PaddleDetectInput
    {
        public TensorFloat? images;
    }

    public class PaddleDetectOutput
    {
        public TensorFloat? outputs;
    }
}
