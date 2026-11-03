using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Boymulator.Graphics.Vulkan;

public sealed unsafe class VulkanRenderer : IDisposable
{
    private readonly IWindow _window;
    private readonly Vk _vk;
    private KhrSurface _khrSurface = null!;
    private KhrSwapchain _khrSwapchain = null!;

    private Instance _instance;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _queue;
    private uint _graphicsQueueIndex;

    private SwapchainKHR _swapchain;
    private Format _swapchainFormat;
    private Extent2D _swapchainExtent;
    private Image[] _swapchainImages = [];
    private ImageView[] _swapchainImageViews = [];
    private Framebuffer[] _framebuffers = [];

    private RenderPass _renderPass;
    private DescriptorSetLayout _descriptorSetLayout;
    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;

    // Emulator Texture (160 x 144)
    public const int GameBoyWidth = 160;
    public const int GameBoyHeight = 144;
    private const int TextureByteSize = GameBoyWidth * GameBoyHeight * 4;

    private Image _textureImage;
    private DeviceMemory _textureImageMemory;
    private ImageView _textureImageView;
    private Sampler _textureSampler;

    private Silk.NET.Vulkan.Buffer _stagingBuffer;
    private DeviceMemory _stagingBufferMemory;
    private void* _stagingBufferMapped;

    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;

    private CommandPool _commandPool;
    private CommandBuffer[] _commandBuffers = [];

    // Synchronization (Double-buffering: 2 frames in flight)
    private const int MaxFramesInFlight = 2;
    private VkSemaphore[] _imageAvailableSemaphores = new VkSemaphore[MaxFramesInFlight];
    private VkSemaphore[] _renderFinishedSemaphores = new VkSemaphore[MaxFramesInFlight];
    private Fence[] _inFlightFences = new Fence[MaxFramesInFlight];
    private int _currentFrame;

    private bool _framebufferResized;
    private bool _isDisposed;

    public VulkanRenderer(IWindow window)
    {
        _window = window;
        _vk = Vk.GetApi();

        InitVulkan();
    }

    public void OnFramebufferResize()
    {
        _framebufferResized = true;
    }

    private void InitVulkan()
    {
        CreateInstance();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapchain();
        CreateImageViews();
        CreateRenderPass();
        CreateDescriptorSetLayout();
        CreateGraphicsPipeline();
        CreateCommandPool();
        CreateTextureImage();
        CreateTextureImageView();
        CreateTextureSampler();
        CreateStagingBuffer();
        CreateDescriptorPool();
        CreateDescriptorSets();
        CreateFramebuffers();
        CreateCommandBuffers();
        CreateSyncObjects();
    }

    private void CreateInstance()
    {
        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)SilkMarshal.StringToPtr("Boymulator"),
            ApplicationVersion = Vk.MakeVersion(1, 0, 0),
            PEngineName = (byte*)SilkMarshal.StringToPtr("Boymulator Engine"),
            EngineVersion = Vk.MakeVersion(1, 0, 0),
            ApiVersion = Vk.Version11
        };

        byte** requiredExtensions = _window.VkSurface!.GetRequiredExtensions(out uint extCount);
        string[] extensions = new string[extCount];
        for (int i = 0; i < extCount; i++)
        {
            extensions[i] = SilkMarshal.PtrToString((nint)requiredExtensions[i])!;
        }

        byte** ppExtensions = (byte**)SilkMarshal.StringArrayToPtr(extensions);

        InstanceCreateInfo createInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = extCount,
            PpEnabledExtensionNames = ppExtensions,
            EnabledLayerCount = 0,
            PpEnabledLayerNames = null
        };

        fixed (Instance* pInstance = &_instance)
        {
            if (_vk.CreateInstance(in createInfo, null, pInstance) != Result.Success)
            {
                throw new Exception("Failed to create Vulkan instance!");
            }
        }

        SilkMarshal.Free((nint)appInfo.PApplicationName);
        SilkMarshal.Free((nint)appInfo.PEngineName);
        SilkMarshal.Free((nint)ppExtensions);

        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
        {
            throw new NotSupportedException("VK_KHR_surface extension not available!");
        }
    }

    private void CreateSurface()
    {
        _surface = _window.VkSurface!.Create<AllocationCallbacks>(new VkHandle((nint)_instance.Handle), null).ToSurface();
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, null);
        if (deviceCount == 0)
        {
            throw new Exception("Failed to find GPUs with Vulkan support!");
        }

        Span<PhysicalDevice> devices = stackalloc PhysicalDevice[(int)deviceCount];
        fixed (PhysicalDevice* pDevices = devices)
        {
            _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, pDevices);
        }

        PhysicalDevice bestDevice = default;
        int bestScore = -1;

        for (int i = 0; i < deviceCount; i++)
        {
            var dev = devices[i];
            int score = RateDevice(dev);
            if (score > bestScore)
            {
                bestScore = score;
                bestDevice = dev;
            }
        }

        if (bestScore < 0)
        {
            throw new Exception("Failed to find a suitable GPU!");
        }

        _physicalDevice = bestDevice;
    }

    private int RateDevice(PhysicalDevice device)
    {
        PhysicalDeviceProperties props;
        _vk.GetPhysicalDeviceProperties(device, &props);

        if (!FindQueueFamilies(device, out uint queueIndex))
        {
            return -1;
        }

        int score = 0;
        if (props.DeviceType == PhysicalDeviceType.DiscreteGpu)
        {
            score += 1000;
        }
        else if (props.DeviceType == PhysicalDeviceType.IntegratedGpu)
        {
            score += 100;
        }

        _graphicsQueueIndex = queueIndex;
        return score;
    }

    private bool FindQueueFamilies(PhysicalDevice device, out uint graphicsQueueIndex)
    {
        graphicsQueueIndex = 0;
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilyCount, null);

        Span<QueueFamilyProperties> families = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
        fixed (QueueFamilyProperties* pFamilies = families)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilyCount, pFamilies);
        }

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((families[(int)i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                _khrSurface.GetPhysicalDeviceSurfaceSupport(device, i, _surface, out var presentSupport);
                if (presentSupport)
                {
                    graphicsQueueIndex = i;
                    return true;
                }
            }
        }

        return false;
    }

    private void CreateLogicalDevice()
    {
        float queuePriority = 1.0f;
        DeviceQueueCreateInfo queueCreateInfo = new()
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _graphicsQueueIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        PhysicalDeviceFeatures deviceFeatures = default;

        string[] deviceExtensions = [KhrSwapchain.ExtensionName];
        byte** ppDeviceExtensions = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions);

        DeviceCreateInfo createInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            PEnabledFeatures = &deviceFeatures,
            EnabledExtensionCount = 1,
            PpEnabledExtensionNames = ppDeviceExtensions
        };

        fixed (Device* pDevice = &_device)
        {
            if (_vk.CreateDevice(_physicalDevice, in createInfo, null, pDevice) != Result.Success)
            {
                throw new Exception("Failed to create logical device!");
            }
        }

        SilkMarshal.Free((nint)ppDeviceExtensions);

        _vk.GetDeviceQueue(_device, _graphicsQueueIndex, 0, out _queue);

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
        {
            throw new NotSupportedException("VK_KHR_swapchain extension not available!");
        }
    }

    private void CreateSwapchain()
    {
        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var capabilities);

        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref formatCount, null);
        Span<SurfaceFormatKHR> formats = stackalloc SurfaceFormatKHR[(int)formatCount];
        fixed (SurfaceFormatKHR* pFormats = formats)
        {
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref formatCount, pFormats);
        }

        SurfaceFormatKHR surfaceFormat = formats[0];
        foreach (var fmt in formats)
        {
            if (fmt.Format == Format.B8G8R8A8Unorm && fmt.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                surfaceFormat = fmt;
                break;
            }
        }

        PresentModeKHR presentMode = PresentModeKHR.FifoKhr; // standard V-Sync

        Extent2D extent;
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            extent = capabilities.CurrentExtent;
        }
        else
        {
            extent = new Extent2D(
                Math.Clamp((uint)_window.FramebufferSize.X, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Math.Clamp((uint)_window.FramebufferSize.Y, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height)
            );
        }

        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
        {
            imageCount = capabilities.MaxImageCount;
        }

        SwapchainCreateInfoKHR createInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = default
        };

        fixed (SwapchainKHR* pSwapchain = &_swapchain)
        {
            if (_khrSwapchain.CreateSwapchain(_device, in createInfo, null, pSwapchain) != Result.Success)
            {
                throw new Exception("Failed to create swapchain!");
            }
        }

        _khrSwapchain.GetSwapchainImages(_device, _swapchain, ref imageCount, null);
        _swapchainImages = new Image[imageCount];
        fixed (Image* pImages = _swapchainImages)
        {
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, ref imageCount, pImages);
        }

        _swapchainFormat = surfaceFormat.Format;
        _swapchainExtent = extent;
    }

    private void CreateImageViews()
    {
        _swapchainImageViews = new ImageView[_swapchainImages.Length];
        for (int i = 0; i < _swapchainImages.Length; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _swapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = _swapchainFormat,
                Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };

            fixed (ImageView* pView = &_swapchainImageViews[i])
            {
                if (_vk.CreateImageView(_device, in createInfo, null, pView) != Result.Success)
                {
                    throw new Exception("Failed to create swapchain image view!");
                }
            }
        }
    }

    private void CreateRenderPass()
    {
        AttachmentDescription colorAttachment = new()
        {
            Format = _swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        AttachmentReference colorAttachmentRef = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef
        };

        SubpassDependency dependency = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        RenderPassCreateInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        fixed (RenderPass* pRenderPass = &_renderPass)
        {
            if (_vk.CreateRenderPass(_device, in renderPassInfo, null, pRenderPass) != Result.Success)
            {
                throw new Exception("Failed to create render pass!");
            }
        }
    }

    private void CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding samplerLayoutBinding = new()
        {
            Binding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImmutableSamplers = null,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &samplerLayoutBinding
        };

        fixed (DescriptorSetLayout* pLayout = &_descriptorSetLayout)
        {
            if (_vk.CreateDescriptorSetLayout(_device, in layoutInfo, null, pLayout) != Result.Success)
            {
                throw new Exception("Failed to create descriptor set layout!");
            }
        }
    }

    private void CreateGraphicsPipeline()
    {
        var vertShaderModule = CreateShaderModule(Shaders.VertexShaderSpv);
        var fragShaderModule = CreateShaderModule(Shaders.FragmentShaderSpv);

        byte* mainName = (byte*)SilkMarshal.StringToPtr("main");

        PipelineShaderStageCreateInfo vertStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertShaderModule,
            PName = mainName
        };

        PipelineShaderStageCreateInfo fragStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragShaderModule,
            PName = mainName
        };

        PipelineShaderStageCreateInfo* shaderStages = stackalloc PipelineShaderStageCreateInfo[2]
        {
            vertStageInfo,
            fragStageInfo
        };

        PipelineVertexInputStateCreateInfo vertexInputInfo = new()
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 0,
            VertexAttributeDescriptionCount = 0
        };

        PipelineInputAssemblyStateCreateInfo inputAssembly = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false
        };

        DynamicState* dynamicStates = stackalloc DynamicState[2]
        {
            DynamicState.Viewport,
            DynamicState.Scissor
        };

        PipelineDynamicStateCreateInfo dynamicState = new()
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        PipelineViewportStateCreateInfo viewportState = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        PipelineRasterizationStateCreateInfo rasterizer = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = false,
            RasterizerDiscardEnable = false,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1.0f,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.CounterClockwise,
            DepthBiasEnable = false
        };

        PipelineMultisampleStateCreateInfo multisampling = new()
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        PipelineColorBlendAttachmentState colorBlendAttachment = new()
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = false
        };

        PipelineColorBlendStateCreateInfo colorBlending = new()
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            LogicOpEnable = false,
            AttachmentCount = 1,
            PAttachments = &colorBlendAttachment
        };

        var setLayout = _descriptorSetLayout;
        PipelineLayoutCreateInfo pipelineLayoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout
        };

        fixed (PipelineLayout* pPipelineLayout = &_pipelineLayout)
        {
            if (_vk.CreatePipelineLayout(_device, in pipelineLayoutInfo, null, pPipelineLayout) != Result.Success)
            {
                throw new Exception("Failed to create pipeline layout!");
            }
        }

        GraphicsPipelineCreateInfo pipelineInfo = new()
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = shaderStages,
            PVertexInputState = &vertexInputInfo,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterizer,
            PMultisampleState = &multisampling,
            PColorBlendState = &colorBlending,
            PDynamicState = &dynamicState,
            Layout = _pipelineLayout,
            RenderPass = _renderPass,
            Subpass = 0
        };

        fixed (Pipeline* pPipeline = &_pipeline)
        {
            if (_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, pPipeline) != Result.Success)
            {
                throw new Exception("Failed to create graphics pipeline!");
            }
        }

        SilkMarshal.Free((nint)mainName);
        _vk.DestroyShaderModule(_device, fragShaderModule, null);
        _vk.DestroyShaderModule(_device, vertShaderModule, null);
    }

    private ShaderModule CreateShaderModule(ReadOnlySpan<byte> code)
    {
        ShaderModuleCreateInfo createInfo = new()
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint)code.Length
        };

        fixed (byte* pCode = code)
        {
            createInfo.PCode = (uint*)pCode;
            _vk.CreateShaderModule(_device, in createInfo, null, out var shaderModule);
            return shaderModule;
        }
    }

    private void CreateFramebuffers()
    {
        _framebuffers = new Framebuffer[_swapchainImageViews.Length];

        for (int i = 0; i < _swapchainImageViews.Length; i++)
        {
            var attachment = _swapchainImageViews[i];
            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = _swapchainExtent.Width,
                Height = _swapchainExtent.Height,
                Layers = 1
            };

            fixed (Framebuffer* pFramebuffer = &_framebuffers[i])
            {
                if (_vk.CreateFramebuffer(_device, in framebufferInfo, null, pFramebuffer) != Result.Success)
                {
                    throw new Exception("Failed to create framebuffer!");
                }
            }
        }
    }

    private void CreateCommandPool()
    {
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = _graphicsQueueIndex
        };

        fixed (CommandPool* pCommandPool = &_commandPool)
        {
            if (_vk.CreateCommandPool(_device, in poolInfo, null, pCommandPool) != Result.Success)
            {
                throw new Exception("Failed to create command pool!");
            }
        }
    }

    private void CreateTextureImage()
    {
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(GameBoyWidth, GameBoyHeight, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = Format.R8G8B8A8Unorm,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            Samples = SampleCountFlags.Count1Bit
        };

        fixed (Image* pImage = &_textureImage)
        {
            if (_vk.CreateImage(_device, in imageInfo, null, pImage) != Result.Success)
            {
                throw new Exception("Failed to create texture image!");
            }
        }

        MemoryRequirements memRequirements;
        _vk.GetImageMemoryRequirements(_device, _textureImage, &memRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        fixed (DeviceMemory* pMemory = &_textureImageMemory)
        {
            if (_vk.AllocateMemory(_device, in allocInfo, null, pMemory) != Result.Success)
            {
                throw new Exception("Failed to allocate texture image memory!");
            }
        }

        _vk.BindImageMemory(_device, _textureImage, _textureImageMemory, 0);

        // Transition image layout to ShaderReadOnlyOptimal initially
        var cmd = BeginSingleTimeCommands();
        TransitionImageLayout(cmd, _textureImage, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal);
        EndSingleTimeCommands(cmd);
    }

    private void CreateTextureImageView()
    {
        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _textureImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };

        fixed (ImageView* pView = &_textureImageView)
        {
            if (_vk.CreateImageView(_device, in viewInfo, null, pView) != Result.Success)
            {
                throw new Exception("Failed to create texture image view!");
            }
        }
    }

    private void CreateTextureSampler()
    {
        SamplerCreateInfo samplerInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest, // Authentic sharp pixel art!
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            AnisotropyEnable = false,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            MipmapMode = SamplerMipmapMode.Nearest
        };

        fixed (Sampler* pSampler = &_textureSampler)
        {
            if (_vk.CreateSampler(_device, in samplerInfo, null, pSampler) != Result.Success)
            {
                throw new Exception("Failed to create texture sampler!");
            }
        }
    }

    private void CreateStagingBuffer()
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = TextureByteSize,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive
        };

        fixed (Silk.NET.Vulkan.Buffer* pBuffer = &_stagingBuffer)
        {
            if (_vk.CreateBuffer(_device, in bufferInfo, null, pBuffer) != Result.Success)
            {
                throw new Exception("Failed to create staging buffer!");
            }
        }

        MemoryRequirements memRequirements;
        _vk.GetBufferMemoryRequirements(_device, _stagingBuffer, &memRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        fixed (DeviceMemory* pMemory = &_stagingBufferMemory)
        {
            if (_vk.AllocateMemory(_device, in allocInfo, null, pMemory) != Result.Success)
            {
                throw new Exception("Failed to allocate staging buffer memory!");
            }
        }

        _vk.BindBufferMemory(_device, _stagingBuffer, _stagingBufferMemory, 0);

        void* mapped;
        _vk.MapMemory(_device, _stagingBufferMemory, 0, TextureByteSize, 0, &mapped);
        _stagingBufferMapped = mapped;
    }

    private void CreateDescriptorPool()
    {
        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = 1
        };

        fixed (DescriptorPool* pPool = &_descriptorPool)
        {
            if (_vk.CreateDescriptorPool(_device, in poolInfo, null, pPool) != Result.Success)
            {
                throw new Exception("Failed to create descriptor pool!");
            }
        }
    }

    private void CreateDescriptorSets()
    {
        var layout = _descriptorSetLayout;
        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };

        fixed (DescriptorSet* pSet = &_descriptorSet)
        {
            if (_vk.AllocateDescriptorSets(_device, in allocInfo, pSet) != Result.Success)
            {
                throw new Exception("Failed to allocate descriptor set!");
            }
        }

        DescriptorImageInfo imageInfo = new()
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _textureImageView,
            Sampler = _textureSampler
        };

        WriteDescriptorSet descriptorWrite = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, in descriptorWrite, 0, null);
    }

    private void CreateCommandBuffers()
    {
        _commandBuffers = new CommandBuffer[MaxFramesInFlight];
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = MaxFramesInFlight
        };

        fixed (CommandBuffer* pBuffers = _commandBuffers)
        {
            if (_vk.AllocateCommandBuffers(_device, in allocInfo, pBuffers) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }
    }

    private void CreateSyncObjects()
    {
        SemaphoreCreateInfo semaphoreInfo = new() { SType = StructureType.SemaphoreCreateInfo };
        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            fixed (VkSemaphore* pAvailable = &_imageAvailableSemaphores[i])
            fixed (VkSemaphore* pFinished = &_renderFinishedSemaphores[i])
            fixed (Fence* pFence = &_inFlightFences[i])
            {
                if (_vk.CreateSemaphore(_device, in semaphoreInfo, null, pAvailable) != Result.Success ||
                    _vk.CreateSemaphore(_device, in semaphoreInfo, null, pFinished) != Result.Success ||
                    _vk.CreateFence(_device, in fenceInfo, null, pFence) != Result.Success)
                {
                    throw new Exception("Failed to create synchronization objects!");
                }
            }
        }
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProperties;
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, &memProperties);

        for (uint i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << (int)i)) != 0 && (memProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new Exception("Failed to find suitable memory type!");
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void RenderFrame(ReadOnlySpan<uint> framebufferPixels)
    {
        var fence = _inFlightFences[_currentFrame];
        _vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue);

        // Upload Game Boy framebuffer pixels to mapped staging buffer
        fixed (uint* src = framebufferPixels)
        {
            Unsafe.CopyBlock(_stagingBufferMapped, src, (uint)TextureByteSize);
        }

        uint imageIndex = 0;
        var result = _khrSwapchain.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref imageIndex);

        if (result == Result.ErrorOutOfDateKhr || _framebufferResized)
        {
            _framebufferResized = false;
            RecreateSwapchain();
            return;
        }
        else if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            throw new Exception("Failed to acquire swapchain image!");
        }

        _vk.ResetFences(_device, 1, in fence);

        var commandBuffer = _commandBuffers[_currentFrame];
        _vk.ResetCommandBuffer(commandBuffer, 0);

        RecordCommandBuffer(commandBuffer, imageIndex);

        var waitSemaphore = _imageAvailableSemaphores[_currentFrame];
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        var signalSemaphore = _renderFinishedSemaphores[_currentFrame];

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore
        };

        if (_vk.QueueSubmit(_queue, 1, in submitInfo, fence) != Result.Success)
        {
            throw new Exception("Failed to submit draw command buffer!");
        }

        var swapchain = _swapchain;
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &signalSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };

        result = _khrSwapchain.QueuePresent(_queue, in presentInfo);
        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr || _framebufferResized)
        {
            _framebufferResized = false;
            RecreateSwapchain();
        }

        _currentFrame = (_currentFrame + 1) % MaxFramesInFlight;
    }

    private void RecordCommandBuffer(CommandBuffer commandBuffer, uint imageIndex)
    {
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo
        };

        _vk.BeginCommandBuffer(commandBuffer, in beginInfo);

        // 1. Copy staging buffer to texture image
        TransitionImageLayout(commandBuffer, _textureImage, ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal);

        BufferImageCopy region = new()
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(GameBoyWidth, GameBoyHeight, 1)
        };

        _vk.CmdCopyBufferToImage(commandBuffer, _stagingBuffer, _textureImage, ImageLayout.TransferDstOptimal, 1, in region);

        TransitionImageLayout(commandBuffer, _textureImage, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        // 2. Render Pass
        ClearValue clearColor = new()
        {
            Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 1.0f) // Black background for letterboxing
        };

        RenderPassBeginInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), _swapchainExtent),
            ClearValueCount = 1,
            PClearValues = &clearColor
        };

        _vk.CmdBeginRenderPass(commandBuffer, in renderPassInfo, SubpassContents.Inline);

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);

        // 3. Compute aspect-ratio letterboxed viewport (10:9 aspect ratio of Game Boy)
        float targetAspect = (float)GameBoyWidth / GameBoyHeight;
        float windowAspect = (float)_swapchainExtent.Width / _swapchainExtent.Height;

        float vpWidth, vpHeight, vpX, vpY;
        if (windowAspect > targetAspect)
        {
            vpHeight = _swapchainExtent.Height;
            vpWidth = vpHeight * targetAspect;
            vpX = (_swapchainExtent.Width - vpWidth) / 2.0f;
            vpY = 0;
        }
        else
        {
            vpWidth = _swapchainExtent.Width;
            vpHeight = vpWidth / targetAspect;
            vpX = 0;
            vpY = (_swapchainExtent.Height - vpHeight) / 2.0f;
        }

        Viewport viewport = new(vpX, vpY, vpWidth, vpHeight, 0.0f, 1.0f);
        _vk.CmdSetViewport(commandBuffer, 0, 1, in viewport);

        Rect2D scissor = new(new Offset2D((int)vpX, (int)vpY), new Extent2D((uint)vpWidth, (uint)vpHeight));
        _vk.CmdSetScissor(commandBuffer, 0, 1, in scissor);

        var descriptorSet = _descriptorSet;
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, in descriptorSet, 0, null);

        // Draw fullscreen triangle (3 vertices)
        _vk.CmdDraw(commandBuffer, 3, 1, 0, 0);

        _vk.CmdEndRenderPass(commandBuffer);

        _vk.EndCommandBuffer(commandBuffer);
    }

    private void TransitionImageLayout(CommandBuffer commandBuffer, Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };

        PipelineStageFlags sourceStage;
        PipelineStageFlags destinationStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            sourceStage = PipelineStageFlags.TransferBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderReadBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            sourceStage = PipelineStageFlags.FragmentShaderBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else
        {
            sourceStage = PipelineStageFlags.AllCommandsBit;
            destinationStage = PipelineStageFlags.AllCommandsBit;
        }

        _vk.CmdPipelineBarrier(commandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, in barrier);
    }

    private CommandBuffer BeginSingleTimeCommands()
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = _commandPool,
            CommandBufferCount = 1
        };

        _vk.AllocateCommandBuffers(_device, in allocInfo, out var commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(commandBuffer, in beginInfo);
        return commandBuffer;
    }

    private void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        _vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        _vk.QueueSubmit(_queue, 1, in submitInfo, default);
        _vk.QueueWaitIdle(_queue);

        _vk.FreeCommandBuffers(_device, _commandPool, 1, in commandBuffer);
    }

    private void RecreateSwapchain()
    {
        while (_window.FramebufferSize.X == 0 || _window.FramebufferSize.Y == 0)
        {
            _window.DoEvents();
        }

        _vk.DeviceWaitIdle(_device);

        CleanupSwapchain();

        CreateSwapchain();
        CreateImageViews();
        CreateFramebuffers();
    }

    private void CleanupSwapchain()
    {
        for (int i = 0; i < _framebuffers.Length; i++)
        {
            _vk.DestroyFramebuffer(_device, _framebuffers[i], null);
        }

        for (int i = 0; i < _swapchainImageViews.Length; i++)
        {
            _vk.DestroyImageView(_device, _swapchainImageViews[i], null);
        }

        _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _vk.DeviceWaitIdle(_device);

        CleanupSwapchain();

        _vk.DestroySampler(_device, _textureSampler, null);
        _vk.DestroyImageView(_device, _textureImageView, null);
        _vk.DestroyImage(_device, _textureImage, null);
        _vk.FreeMemory(_device, _textureImageMemory, null);

        if (_stagingBufferMapped != null)
        {
            _vk.UnmapMemory(_device, _stagingBufferMemory);
            _stagingBufferMapped = null;
        }

        _vk.DestroyBuffer(_device, _stagingBuffer, null);
        _vk.FreeMemory(_device, _stagingBufferMemory, null);

        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
        _vk.DestroyPipeline(_device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        _vk.DestroyRenderPass(_device, _renderPass, null);

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            _vk.DestroySemaphore(_device, _renderFinishedSemaphores[i], null);
            _vk.DestroySemaphore(_device, _imageAvailableSemaphores[i], null);
            _vk.DestroyFence(_device, _inFlightFences[i], null);
        }

        _vk.DestroyCommandPool(_device, _commandPool, null);
        _vk.DestroyDevice(_device, null);

        _khrSurface.DestroySurface(_instance, _surface, null);
        _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
    }
}

