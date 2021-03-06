// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

/*=============================================================================
**
**
**
** Purpose: Domains represent an application within the runtime. Objects can 
**          not be shared between domains and each domain can be configured
**          independently. 
**
**
=============================================================================*/

namespace System
{
    using System;
    using System.Reflection;
    using System.Runtime;
    using System.Runtime.CompilerServices;
    using System.Security;
    using System.Security.Permissions;
    using System.Security.Policy;
    using System.Security.Util;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using System.Runtime.InteropServices;
    using System.Runtime.Remoting;
    using System.Reflection.Emit;
    using CultureInfo = System.Globalization.CultureInfo;
    using System.IO;
    using AssemblyHashAlgorithm = System.Configuration.Assemblies.AssemblyHashAlgorithm;
    using System.Text;
    using System.Runtime.ConstrainedExecution;
    using System.Runtime.Versioning;
    using System.Diagnostics.Contracts;
#if FEATURE_EXCEPTION_NOTIFICATIONS
    using System.Runtime.ExceptionServices;
#endif // FEATURE_EXCEPTION_NOTIFICATIONS

    [ComVisible(true)]
    public class ResolveEventArgs : EventArgs
    {
        private String _Name;
        private Assembly _RequestingAssembly;

        public String Name {
            get {
                return _Name;
            }
        }

        public Assembly RequestingAssembly 
        {
            get
            {
                return _RequestingAssembly;
            }
        }

        public ResolveEventArgs(String name)
        {
            _Name = name;
        }

        public ResolveEventArgs(String name, Assembly requestingAssembly)
        {
            _Name = name;
            _RequestingAssembly = requestingAssembly;
        }
    }

    [ComVisible(true)]
    public class AssemblyLoadEventArgs : EventArgs
    {
        private Assembly _LoadedAssembly;

        public Assembly LoadedAssembly {
            get {
                return _LoadedAssembly;
            }
        }

        public AssemblyLoadEventArgs(Assembly loadedAssembly)
        {
            _LoadedAssembly = loadedAssembly;
        }
    }

    [System.Security.SecurityCritical] // auto-generated
    [Serializable]
    [ComVisible(true)]
    public delegate Assembly ResolveEventHandler(Object sender, ResolveEventArgs args);

    [Serializable]
    [ComVisible(true)]
    public delegate void AssemblyLoadEventHandler(Object sender, AssemblyLoadEventArgs args);

    [Serializable]
    [ComVisible(true)]
    public delegate void AppDomainInitializer(string[] args);

    internal class AppDomainInitializerInfo
    {
        internal class ItemInfo
        {
            public string TargetTypeAssembly;
            public string TargetTypeName;
            public string MethodName;
        }

        internal ItemInfo[] Info;

        internal AppDomainInitializerInfo(AppDomainInitializer init)
        {
            Info=null;
            if (init==null)
                return;
            List<ItemInfo> itemInfo = new List<ItemInfo>();
            List<AppDomainInitializer> nestedDelegates = new List<AppDomainInitializer>();
            nestedDelegates.Add(init);
            int idx=0;
 
            while (nestedDelegates.Count>idx)
            {
                AppDomainInitializer curr = nestedDelegates[idx++];
                Delegate[] list= curr.GetInvocationList();
                for (int i=0;i<list.Length;i++)
                {
                    if (!list[i].Method.IsStatic) 
                    {
                        if(list[i].Target==null)
                            continue;
                    
                        AppDomainInitializer nested = list[i].Target as AppDomainInitializer;
                        if (nested!=null)
                            nestedDelegates.Add(nested);
                        else
                            throw new ArgumentException(Environment.GetResourceString("Arg_MustBeStatic"),
                               list[i].Method.ReflectedType.FullName+"::"+list[i].Method.Name);
                    }
                    else
                    {
                        ItemInfo info=new ItemInfo();
                        info.TargetTypeAssembly=list[i].Method.ReflectedType.Module.Assembly.FullName;
                        info.TargetTypeName=list[i].Method.ReflectedType.FullName;
                        info.MethodName=list[i].Method.Name;
                        itemInfo.Add(info);
                    }
                    
                }
            }

            Info = itemInfo.ToArray();            
        }
        
        [System.Security.SecuritySafeCritical]  // auto-generated
        internal AppDomainInitializer Unwrap()
        {
            if (Info==null)
                return null;
            AppDomainInitializer retVal=null;
            new ReflectionPermission(ReflectionPermissionFlag.MemberAccess).Assert();
            for (int i=0;i<Info.Length;i++)
            {
                Assembly assembly=Assembly.Load(Info[i].TargetTypeAssembly);
                AppDomainInitializer newVal=(AppDomainInitializer)Delegate.CreateDelegate(typeof(AppDomainInitializer),
                        assembly.GetType(Info[i].TargetTypeName),
                        Info[i].MethodName);
                if(retVal==null)
                    retVal=newVal;
                else
                    retVal+=newVal;
            }
            return retVal;
        }
    }


    [ClassInterface(ClassInterfaceType.None)]
    [ComDefaultInterface(typeof(System._AppDomain))]
    [ComVisible(true)]
    public sealed class AppDomain :
        _AppDomain, IEvidenceFactory
    {
        // Domain security information
        // These fields initialized from the other side only. (NOTE: order 
        // of these fields cannot be changed without changing the layout in 
        // the EE- AppDomainBaseObject in this case)

        [System.Security.SecurityCritical] // auto-generated
        private AppDomainManager _domainManager;
        private Dictionary<String, Object[]> _LocalStore;
        private AppDomainSetup   _FusionStore;
        private Evidence         _SecurityIdentity;
#pragma warning disable 169
        private Object[]         _Policies; // Called from the VM.
#pragma warning restore 169
        [method: System.Security.SecurityCritical]
        public event AssemblyLoadEventHandler AssemblyLoad;

        [System.Security.SecurityCritical]
        private ResolveEventHandler _TypeResolve;

        public event ResolveEventHandler TypeResolve
        {
            [System.Security.SecurityCritical]
            add
            {
                lock (this)
                {
                    _TypeResolve += value;
                }
            }

            [System.Security.SecurityCritical]
            remove
            {
                lock (this)
                {
                    _TypeResolve -= value;
                }
            }
        }

        [System.Security.SecurityCritical]
        private ResolveEventHandler _ResourceResolve;

        public event ResolveEventHandler ResourceResolve
        {
            [System.Security.SecurityCritical]
            add
            {
                lock (this)
                {
                    _ResourceResolve += value;
                }
            }

            [System.Security.SecurityCritical]
            remove
            {
                lock (this)
                {
                    _ResourceResolve -= value;
                }
            }
        }

        [System.Security.SecurityCritical]
        private ResolveEventHandler _AssemblyResolve;

        public event ResolveEventHandler AssemblyResolve
        {
            [System.Security.SecurityCritical]
            add
            {
                lock (this)
                {
                    _AssemblyResolve += value;
                }
            }

            [System.Security.SecurityCritical]
            remove
            {
                lock (this)
                {
                    _AssemblyResolve -= value;
                }
            }
        }

#if FEATURE_REFLECTION_ONLY_LOAD
        [method: System.Security.SecurityCritical]
        public event ResolveEventHandler ReflectionOnlyAssemblyResolve;
#endif // FEATURE_REFLECTION_ONLY

        private ApplicationTrust _applicationTrust;
        private EventHandler     _processExit;

        [System.Security.SecurityCritical] 
        private EventHandler     _domainUnload;

        [System.Security.SecurityCritical] // auto-generated
        private UnhandledExceptionEventHandler _unhandledException;

        // The compat flags are set at domain creation time to indicate that the given breaking
        // changes (named in the strings) should not be used in this domain. We only use the 
        // keys, the vhe values are ignored.
        private Dictionary<String, object>  _compatFlags;

#if FEATURE_EXCEPTION_NOTIFICATIONS
        // Delegate that will hold references to FirstChance exception notifications
        private EventHandler<FirstChanceExceptionEventArgs> _firstChanceException;
#endif // FEATURE_EXCEPTION_NOTIFICATIONS

        private IntPtr           _pDomain;                      // this is an unmanaged pointer (AppDomain * m_pDomain)` used from the VM.

        private bool             _HasSetPolicy;
        private bool             _IsFastFullTrustDomain;        // quick check to see if the AppDomain is fully trusted and homogenous
        private bool             _compatFlagsInitialized;

        internal const String TargetFrameworkNameAppCompatSetting = "TargetFrameworkName";

#if FEATURE_APPX
        private static APPX_FLAGS s_flags;

        //
        // Keep in async with vm\appdomainnative.cpp
        //
        [Flags]
        private enum APPX_FLAGS
        {
            APPX_FLAGS_INITIALIZED =        0x01,

            APPX_FLAGS_APPX_MODEL =         0x02,
            APPX_FLAGS_APPX_DESIGN_MODE =   0x04,
            APPX_FLAGS_APPX_NGEN =          0x08,
            APPX_FLAGS_APPX_MASK =          APPX_FLAGS_APPX_MODEL |
                                            APPX_FLAGS_APPX_DESIGN_MODE |
                                            APPX_FLAGS_APPX_NGEN,

            APPX_FLAGS_API_CHECK =          0x10,
        }

        private static APPX_FLAGS Flags
        {
            [SecuritySafeCritical]
            get
            {
                if (s_flags == 0)
                    s_flags = nGetAppXFlags();

                Contract.Assert(s_flags != 0);
                return s_flags;
            }
        }

        internal static bool ProfileAPICheck
        {
            [SecuritySafeCritical]
            get
            {
                return (Flags & APPX_FLAGS.APPX_FLAGS_API_CHECK) != 0;
            }
        }

        internal static bool IsAppXNGen
        {
            [SecuritySafeCritical]
            get
            {
                return (Flags & APPX_FLAGS.APPX_FLAGS_APPX_NGEN) != 0;
            }
        }
#endif // FEATURE_APPX

        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        [SecurityCritical]
        [SuppressUnmanagedCodeSecurity]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DisableFusionUpdatesFromADManager(AppDomainHandle domain);

#if FEATURE_APPX
        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        [SecurityCritical]
        [SuppressUnmanagedCodeSecurity]
        [return: MarshalAs(UnmanagedType.I4)]
        private static extern APPX_FLAGS nGetAppXFlags();
#endif

        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        [SecurityCritical]
        [SuppressUnmanagedCodeSecurity]
        private static extern void GetAppDomainManagerType(AppDomainHandle domain,
                                                           StringHandleOnStack retAssembly,
                                                           StringHandleOnStack retType);

        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        [SecurityCritical]
        [SuppressUnmanagedCodeSecurity]
        private static extern void SetAppDomainManagerType(AppDomainHandle domain,
                                                           string assembly,
                                                           string type);

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private static extern void nSetHostSecurityManagerFlags (HostSecurityManagerOptions flags);

        [SecurityCritical]
        [SuppressUnmanagedCodeSecurity]
        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        private static extern void SetSecurityHomogeneousFlag(AppDomainHandle domain,
                                                              [MarshalAs(UnmanagedType.Bool)] bool runtimeSuppliedHomogenousGrantSet);

        /// <summary>
        ///     Get a handle used to make a call into the VM pointing to this domain
        /// </summary>
        internal AppDomainHandle GetNativeHandle()
        {
            // This should never happen under normal circumstances. However, there ar ways to create an
            // uninitialized object through remoting, etc.
            if (_pDomain.IsNull())
            {
                throw new InvalidOperationException(Environment.GetResourceString("Argument_InvalidHandle"));
            }

            return new AppDomainHandle(_pDomain);
        }

        /// <summary>
        ///     If this AppDomain is configured to have an AppDomain manager then create the instance of it.
        ///     This method is also called from the VM to create the domain manager in the default domain.
        /// </summary>
        [SecuritySafeCritical]
        private void CreateAppDomainManager()
        {
            Contract.Assert(_domainManager == null, "_domainManager == null");

            AppDomainSetup adSetup = FusionStore;
#if FEATURE_VERSIONING
            String trustedPlatformAssemblies = (String)(GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
            if (trustedPlatformAssemblies != null)
            {
                String platformResourceRoots = (String)(GetData("PLATFORM_RESOURCE_ROOTS"));
                if (platformResourceRoots == null)
                {
                    platformResourceRoots = String.Empty;
                }

                String appPaths = (String)(GetData("APP_PATHS"));
                if (appPaths == null)
                {
                    appPaths = String.Empty;
                }

                String appNiPaths = (String)(GetData("APP_NI_PATHS"));
                if (appNiPaths == null)
                {
                    appNiPaths = String.Empty;
                }

                String appLocalWinMD = (String)(GetData("APP_LOCAL_WINMETADATA"));
                if (appLocalWinMD == null)
                {
                    appLocalWinMD = String.Empty;
                }
                SetupBindingPaths(trustedPlatformAssemblies, platformResourceRoots, appPaths, appNiPaths, appLocalWinMD);
            }
#endif // FEATURE_VERSIONING

            string domainManagerAssembly;
            string domainManagerType;
            GetAppDomainManagerType(out domainManagerAssembly, out domainManagerType);

            if (domainManagerAssembly != null && domainManagerType != null)
            {
                try
                {
                    new PermissionSet(PermissionState.Unrestricted).Assert();
                    _domainManager = CreateInstanceAndUnwrap(domainManagerAssembly, domainManagerType) as AppDomainManager;
                    CodeAccessPermission.RevertAssert();
                }
                catch (FileNotFoundException e)
                {
                    throw new TypeLoadException(Environment.GetResourceString("Argument_NoDomainManager"), e);
                }
                catch (SecurityException e)
                {
                    throw new TypeLoadException(Environment.GetResourceString("Argument_NoDomainManager"), e);
                }
                catch (TypeLoadException e)
                {
                    throw new TypeLoadException(Environment.GetResourceString("Argument_NoDomainManager"), e);
                }

                if (_domainManager == null)
                {
                    throw new TypeLoadException(Environment.GetResourceString("Argument_NoDomainManager"));
                }

                // If this domain was not created by a managed call to CreateDomain, then the AppDomainSetup
                // will not have the correct values for the AppDomainManager set.
                FusionStore.AppDomainManagerAssembly = domainManagerAssembly;
                FusionStore.AppDomainManagerType = domainManagerType;

                bool notifyFusion = _domainManager.GetType() != typeof(System.AppDomainManager) && !DisableFusionUpdatesFromADManager();



                AppDomainSetup FusionStoreOld = null;
                if (notifyFusion)
                    FusionStoreOld = new AppDomainSetup(FusionStore, true);

                // Initialize the AppDomainMAnager and register the instance with the native host if requested
                _domainManager.InitializeNewDomain(FusionStore);

                if (notifyFusion)
                    SetupFusionStore(_FusionStore, FusionStoreOld); // Notify Fusion about the changes the user implementation of InitializeNewDomain may have made to the FusionStore object.
            }

            InitializeCompatibilityFlags();
        }

        /// <summary>
        ///     Initialize the compatibility flags to non-NULL values.
        ///     This method is also called from the VM when the default domain dosen't have a domain manager.
        /// </summary>
        private void InitializeCompatibilityFlags()
        {
            AppDomainSetup adSetup = FusionStore;
            
            // set up shim flags regardless of whether we create a DomainManager in this method.
            if (adSetup.GetCompatibilityFlags() != null)
            {
                _compatFlags = new Dictionary<String, object>(adSetup.GetCompatibilityFlags(), StringComparer.OrdinalIgnoreCase);
            }

            // for perf, we don't intialize the _compatFlags dictionary when we don't need to.  However, we do need to make a 
            // note that we've run this method, because IsCompatibilityFlagsSet needs to return different values for the
            // case where the compat flags have been setup.
            Contract.Assert(!_compatFlagsInitialized);
            _compatFlagsInitialized = true;

            CompatibilitySwitches.InitializeSwitches();
        }

        // Retrieves a possibly-cached target framework name for this appdomain.  This could be set
        // either by a host in native, a host in managed using an AppDomainSetup, or by the 
        // TargetFrameworkAttribute on the executable (VS emits its target framework moniker using this
        // attribute starting in version 4).
        [SecuritySafeCritical]
        internal String GetTargetFrameworkName()
        {
            String targetFrameworkName = _FusionStore.TargetFrameworkName;

            if (targetFrameworkName == null && IsDefaultAppDomain() && !_FusionStore.CheckedForTargetFrameworkName)
            {
                // This should only be run in the default appdomain.  All other appdomains should have
                // values copied from the default appdomain and/or specified by the host.
                Assembly assembly = Assembly.GetEntryAssembly();
                if (assembly != null)
                {
                    TargetFrameworkAttribute[] attrs = (TargetFrameworkAttribute[])assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute));
                    if (attrs != null && attrs.Length > 0)
                    {
                        Contract.Assert(attrs.Length == 1);
                        targetFrameworkName = attrs[0].FrameworkName;
                        _FusionStore.TargetFrameworkName = targetFrameworkName;
                    }
                }
                _FusionStore.CheckedForTargetFrameworkName = true;
            }

            return targetFrameworkName;
        }

        /// <summary>
        ///     Returns the setting of the corresponding compatibility config switch (see CreateAppDomainManager for the impact).
        /// </summary>
        [SecuritySafeCritical]
        internal bool DisableFusionUpdatesFromADManager()
        {
            return DisableFusionUpdatesFromADManager(GetNativeHandle());
        }

        /// <summary>
        ///     Returns whether the current AppDomain follows the AppX rules.
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static bool IsAppXModel()
        {
#if FEATURE_APPX
            return (Flags & APPX_FLAGS.APPX_FLAGS_APPX_MODEL) != 0;
#else
            return false;
#endif
        }

        /// <summary>
        ///     Returns the setting of the AppXDevMode config switch.
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static bool IsAppXDesignMode()
        {
#if FEATURE_APPX
            return (Flags & APPX_FLAGS.APPX_FLAGS_APPX_MASK) == (APPX_FLAGS.APPX_FLAGS_APPX_MODEL | APPX_FLAGS.APPX_FLAGS_APPX_DESIGN_MODE);
#else
            return false;
#endif
        }

        /// <summary>
        ///     Checks (and throws on failure) if the domain supports Assembly.LoadFrom.
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static void CheckLoadFromSupported()
        {
#if FEATURE_APPX
            if (IsAppXModel())
                throw new NotSupportedException(Environment.GetResourceString("NotSupported_AppX", "Assembly.LoadFrom"));
#endif
        }

        /// <summary>
        ///     Checks (and throws on failure) if the domain supports Assembly.LoadFile.
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static void CheckLoadFileSupported()
        {
#if FEATURE_APPX
            if (IsAppXModel())
                throw new NotSupportedException(Environment.GetResourceString("NotSupported_AppX", "Assembly.LoadFile"));
#endif
        }

        /// <summary>
        ///     Checks (and throws on failure) if the domain supports Assembly.ReflectionOnlyLoad.
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static void CheckReflectionOnlyLoadSupported()
        {
#if FEATURE_APPX
            if (IsAppXModel())
                throw new NotSupportedException(Environment.GetResourceString("NotSupported_AppX", "Assembly.ReflectionOnlyLoad"));
#endif
        }

        /// <summary>
        ///     Checks (and throws on failure) if the domain supports Assembly.LoadWithPartialName.
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static void CheckLoadWithPartialNameSupported(StackCrawlMark stackMark)
        {
#if FEATURE_APPX
            if (IsAppXModel())
            {
                RuntimeAssembly callingAssembly = RuntimeAssembly.GetExecutingAssembly(ref stackMark);
                bool callerIsFxAssembly = callingAssembly != null && callingAssembly.IsFrameworkAssembly();
                if (!callerIsFxAssembly)
                {
                    throw new NotSupportedException(Environment.GetResourceString("NotSupported_AppX", "Assembly.LoadWithPartialName"));
                }
            }
#endif
        }

        /// <summary>
        ///     Checks (and throws on failure) if the domain supports DefinePInvokeMethod.
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static void CheckDefinePInvokeSupported()
        {
            // We don't want users to use DefinePInvokeMethod in RefEmit to bypass app store validation on allowed native libraries.
#if FEATURE_APPX
            if (IsAppXModel())
                throw new NotSupportedException(Environment.GetResourceString("NotSupported_AppX", "DefinePInvokeMethod"));
#endif
        }

        /// <summary>
        ///     Checks (and throws on failure) if the domain supports Assembly.Load(byte[] ...).
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static void CheckLoadByteArraySupported()
        {
#if FEATURE_APPX
            if (IsAppXModel())
                throw new NotSupportedException(Environment.GetResourceString("NotSupported_AppX", "Assembly.Load(byte[], ...)"));
#endif
        }

        /// <summary>
        ///     Checks (and throws on failure) if the domain supports AppDomain.CreateDomain.
        /// </summary>
        [SecuritySafeCritical]
        [Pure]
        internal static void CheckCreateDomainSupported()
        {
#if FEATURE_APPX
            // Can create a new domain in an AppX process only when DevMode is enabled and
            // AssemblyLoadingCompat is not enabled (since there is no multi-domain support
            // for LoadFrom and LoadFile in AppX.
            if(IsAppXModel())
            {
                if (!IsAppXDesignMode())
                {
                    throw new NotSupportedException(Environment.GetResourceString("NotSupported_AppX", "AppDomain.CreateDomain"));
                }
            }
#endif
        }

        /// <summary>
        ///     Get the name of the assembly and type that act as the AppDomainManager for this domain
        /// </summary>
        [SecuritySafeCritical]
        internal void GetAppDomainManagerType(out string assembly, out string type)
        {
            // We can't just use our parameters because we need to ensure that the strings used for hte QCall
            // are on the stack.
            string localAssembly = null;
            string localType = null;

            GetAppDomainManagerType(GetNativeHandle(),
                                    JitHelpers.GetStringHandleOnStack(ref localAssembly),
                                    JitHelpers.GetStringHandleOnStack(ref localType));

            assembly = localAssembly;
            type = localType;
        }

        /// <summary>
        ///     Set the assembly and type which act as the AppDomainManager for this domain
        /// </summary>
        [SecuritySafeCritical]
        private void SetAppDomainManagerType(string assembly, string type)
        {
            Contract.Assert(assembly != null, "assembly != null");
            Contract.Assert(type != null, "type != null");
            SetAppDomainManagerType(GetNativeHandle(), assembly, type);
        }

        /// <summary>
        ///     Called for every AppDomain (including the default domain) to initialize the security of the AppDomain)
        /// </summary>
        [SecurityCritical]
        private void InitializeDomainSecurity(Evidence providedSecurityInfo,
                                              Evidence creatorsSecurityInfo,
                                              bool generateDefaultEvidence,
                                              IntPtr parentSecurityDescriptor,
                                              bool publishAppDomain)
        {
            AppDomainSetup adSetup = FusionStore;

            bool runtimeSuppliedHomogenousGrant = false;
            ApplicationTrust appTrust = adSetup.ApplicationTrust;

            if (appTrust != null) {
                SetupDomainSecurityForHomogeneousDomain(appTrust, runtimeSuppliedHomogenousGrant);
            }
            else if (_IsFastFullTrustDomain) {
                SetSecurityHomogeneousFlag(GetNativeHandle(), runtimeSuppliedHomogenousGrant);
            }

            // Get the evidence supplied for the domain.  If no evidence was supplied, it means that we want
            // to use the default evidence creation strategy for this domain
            Evidence newAppDomainEvidence = (providedSecurityInfo != null ? providedSecurityInfo : creatorsSecurityInfo);
            if (newAppDomainEvidence == null && generateDefaultEvidence) {
                newAppDomainEvidence = new Evidence();
            }

            // Set the evidence on the managed side
            _SecurityIdentity = newAppDomainEvidence;

            // Set the evidence of the AppDomain in the VM.
            // Also, now that the initialization is complete, signal that to the security system.
            // Finish the AppDomain initialization and resolve the policy for the AppDomain evidence.
            SetupDomainSecurity(newAppDomainEvidence,
                                parentSecurityDescriptor,
                                publishAppDomain);
        }

        [System.Security.SecurityCritical]  // auto-generated
        private void SetupDomainSecurityForHomogeneousDomain(ApplicationTrust appTrust,
                                                             bool runtimeSuppliedHomogenousGrantSet)
        {
            // If the CLR has supplied the homogenous grant set (that is, this domain would have been
            // heterogenous in v2.0), then we need to strip the ApplicationTrust from the AppDomainSetup of
            // the current domain.  This prevents code which does:
            //   AppDomain.CreateDomain(..., AppDomain.CurrentDomain.SetupInformation);
            // 
            // From looking like it is trying to create a homogenous domain intentionally, and therefore
            // having its evidence check bypassed.
            if (runtimeSuppliedHomogenousGrantSet)
            {
                BCLDebug.Assert(_FusionStore.ApplicationTrust != null, "Expected to find runtime supplied ApplicationTrust");
            }

            _applicationTrust = appTrust;

            // Set the homogeneous bit in the VM's ApplicationSecurityDescriptor.
            SetSecurityHomogeneousFlag(GetNativeHandle(),
                                       runtimeSuppliedHomogenousGrantSet);
        }

        public AppDomainManager DomainManager {
            [System.Security.SecurityCritical]  // auto-generated_required
            get {
                return _domainManager;
            }
        }

#if FEATURE_REFLECTION_ONLY_LOAD
        private Assembly ResolveAssemblyForIntrospection(Object sender, ResolveEventArgs args)
        {
            Contract.Requires(args != null);
            return Assembly.ReflectionOnlyLoad(ApplyPolicy(args.Name));
        }
        
        // Helper class for method code:EnableResolveAssembliesForIntrospection
        private class NamespaceResolverForIntrospection
        {
            private IEnumerable<string> _packageGraphFilePaths;
            public NamespaceResolverForIntrospection(IEnumerable<string> packageGraphFilePaths)
            {
                _packageGraphFilePaths = packageGraphFilePaths;
            }
            
            [System.Security.SecurityCritical]
            public void ResolveNamespace(
                object sender, 
                System.Runtime.InteropServices.WindowsRuntime.NamespaceResolveEventArgs args)
            {
                Contract.Requires(args != null);
                
                IEnumerable<string> fileNames = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMetadata.ResolveNamespace(
                    args.NamespaceName,
                    null,   // windowsSdkFilePath ... Use OS installed .winmd files
                    _packageGraphFilePaths);
                foreach (string fileName in fileNames)
                {
                    args.ResolvedAssemblies.Add(Assembly.ReflectionOnlyLoadFrom(fileName));
                }
            }
        }
        
        // Called only by native function code:ValidateWorker
        [System.Security.SecuritySafeCritical]
        private void EnableResolveAssembliesForIntrospection(string verifiedFileDirectory)
        {
            CurrentDomain.ReflectionOnlyAssemblyResolve += new ResolveEventHandler(ResolveAssemblyForIntrospection);
            
            string[] packageGraphFilePaths = null;
            if (verifiedFileDirectory != null)
                packageGraphFilePaths = new string[] { verifiedFileDirectory };
            NamespaceResolverForIntrospection namespaceResolver = new NamespaceResolverForIntrospection(packageGraphFilePaths);
            
            System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMetadata.ReflectionOnlyNamespaceResolve += 
                new EventHandler<System.Runtime.InteropServices.WindowsRuntime.NamespaceResolveEventArgs>(namespaceResolver.ResolveNamespace);
        }
#endif // FEATURE_REFLECTION_ONLY_LOAD


        /**********************************************
        * If an AssemblyName has a public key specified, the assembly is assumed
        * to have a strong name and a hash will be computed when the assembly
        * is saved.
        **********************************************/
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name, access, null,
                                                 null, null, null, null, ref stackMark, null, SecurityContextSource.CurrentAssembly);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            IEnumerable<CustomAttributeBuilder> assemblyAttributes)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name,
                                                 access,
                                                 null, null, null, null, null,
                                                 ref stackMark,
                                                 assemblyAttributes, SecurityContextSource.CurrentAssembly);
        }

        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Due to the stack crawl mark
        [SecuritySafeCritical]
        public AssemblyBuilder DefineDynamicAssembly(AssemblyName name,
                                                     AssemblyBuilderAccess access,
                                                     IEnumerable<CustomAttributeBuilder> assemblyAttributes,
                                                     SecurityContextSource securityContextSource)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name,
                                                 access,
                                                 null, null, null, null, null,
                                                 ref stackMark,
                                                 assemblyAttributes,
                                                 securityContextSource);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            String                  dir)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name, access, dir,
                                                 null, null, null, null,
                                                 ref stackMark,
                                                 null,
                                                 SecurityContextSource.CurrentAssembly);
        }
    
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default.  See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            Evidence                evidence)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name, access, null,
                                                 evidence, null, null, null,
                                                 ref stackMark,
                                                 null,
                                                 SecurityContextSource.CurrentAssembly);
        }
    
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default.  See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            PermissionSet           requiredPermissions,
            PermissionSet           optionalPermissions,
            PermissionSet           refusedPermissions)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name, access, null, null,
                                                 requiredPermissions,
                                                 optionalPermissions,
                                                 refusedPermissions,
                                                 ref stackMark,
                                                 null,
                                                 SecurityContextSource.CurrentAssembly);
        }
    
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of DefineDynamicAssembly which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkId=155570 for more information.")]
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            String                  dir,
            Evidence                evidence)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name, access, dir, evidence,
                                                 null, null, null, ref stackMark, null, SecurityContextSource.CurrentAssembly);
        }
    
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            String                  dir,
            PermissionSet           requiredPermissions,
            PermissionSet           optionalPermissions,
            PermissionSet           refusedPermissions)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name, access, dir, null,
                                                 requiredPermissions,
                                                 optionalPermissions,
                                                 refusedPermissions,
                                                 ref stackMark,
                                                 null,
                                                 SecurityContextSource.CurrentAssembly);
        }
    
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            Evidence                evidence,
            PermissionSet           requiredPermissions,
            PermissionSet           optionalPermissions,
            PermissionSet           refusedPermissions)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name, access, null,
                                                 evidence,
                                                 requiredPermissions,
                                                 optionalPermissions,
                                                 refusedPermissions,
                                                 ref stackMark,
                                                 null,
                                                 SecurityContextSource.CurrentAssembly);
        }
    
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default.  Please see http://go.microsoft.com/fwlink/?LinkId=155570 for more information.")]
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            String                  dir,
            Evidence                evidence,
            PermissionSet           requiredPermissions,
            PermissionSet           optionalPermissions,
            PermissionSet           refusedPermissions)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name, access, dir,
                                                 evidence,
                                                 requiredPermissions,
                                                 optionalPermissions,
                                                 refusedPermissions,
                                                 ref stackMark,
                                                 null,
                                                 SecurityContextSource.CurrentAssembly);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public AssemblyBuilder DefineDynamicAssembly(
            AssemblyName            name,
            AssemblyBuilderAccess   access,
            String                  dir,
            Evidence                evidence,
            PermissionSet           requiredPermissions,
            PermissionSet           optionalPermissions,
            PermissionSet           refusedPermissions,
            bool                    isSynchronized)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name,
                                                 access,
                                                 dir,
                                                 evidence,
                                                 requiredPermissions,
                                                 optionalPermissions,
                                                 refusedPermissions,
                                                 ref stackMark,
                                                 null,
                                                 SecurityContextSource.CurrentAssembly);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public AssemblyBuilder DefineDynamicAssembly(
                    AssemblyName name,
                    AssemblyBuilderAccess access,
                    String dir,
                    Evidence evidence,
                    PermissionSet requiredPermissions,
                    PermissionSet optionalPermissions,
                    PermissionSet refusedPermissions,
                    bool isSynchronized,
                    IEnumerable<CustomAttributeBuilder> assemblyAttributes)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name,
                                                 access,
                                                 dir,
                                                 evidence,
                                                 requiredPermissions,
                                                 optionalPermissions,
                                                 refusedPermissions,
                                                 ref stackMark,
                                                 assemblyAttributes,
                                                 SecurityContextSource.CurrentAssembly);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public AssemblyBuilder DefineDynamicAssembly(
                    AssemblyName name,
                    AssemblyBuilderAccess access,
                    String dir,
                    bool isSynchronized,
                    IEnumerable<CustomAttributeBuilder> assemblyAttributes)
        {
            Contract.Ensures(Contract.Result<AssemblyBuilder>() != null);

            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return InternalDefineDynamicAssembly(name,
                                                 access,
                                                 dir,
                                                 null,
                                                 null,
                                                 null,
                                                 null,
                                                 ref stackMark,
                                                 assemblyAttributes,
                                                 SecurityContextSource.CurrentAssembly);
        }

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        private AssemblyBuilder InternalDefineDynamicAssembly(
            AssemblyName name,
            AssemblyBuilderAccess access,
            String dir,
            Evidence evidence,
            PermissionSet requiredPermissions,
            PermissionSet optionalPermissions,
            PermissionSet refusedPermissions,
            ref StackCrawlMark stackMark,
            IEnumerable<CustomAttributeBuilder> assemblyAttributes,
            SecurityContextSource securityContextSource)
        {
            return AssemblyBuilder.InternalDefineDynamicAssembly(name,
                                                                 access,
                                                                 dir,
                                                                 evidence,
                                                                 requiredPermissions,
                                                                 optionalPermissions,
                                                                 refusedPermissions,
                                                                 ref stackMark,
                                                                 assemblyAttributes,
                                                                 securityContextSource);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern String nApplyPolicy(AssemblyName an);
       
        // Return the assembly name that results from applying policy.
        [ComVisible(false)]
        public String ApplyPolicy(String assemblyName)
        {
            AssemblyName asmName = new AssemblyName(assemblyName);  

            byte[] pk = asmName.GetPublicKeyToken();
            if (pk == null)
                pk = asmName.GetPublicKey();

            // Simply-named assemblies cannot have policy, so for those,
            // we simply return the passed-in assembly name.
            if ((pk == null) || (pk.Length == 0))
                return assemblyName;
            else
                return nApplyPolicy(asmName);
        }

        public ObjectHandle CreateInstance(String assemblyName,
                                           String typeName)
                                         
        {
            // jit does not check for that, so we should do it ...
            if (this == null)
                throw new NullReferenceException();

            if (assemblyName == null)
                throw new ArgumentNullException(nameof(assemblyName));
            Contract.EndContractBlock();

            return Activator.CreateInstance(assemblyName,
                                            typeName);
        }

        [System.Security.SecurityCritical]  // auto-generated
        internal ObjectHandle InternalCreateInstanceWithNoSecurity (string assemblyName, string typeName) {
            PermissionSet.s_fullTrust.Assert();
            return CreateInstance(assemblyName, typeName);
        }

        public ObjectHandle CreateInstanceFrom(String assemblyFile,
                                               String typeName)
                                         
        {
            // jit does not check for that, so we should do it ...
            if (this == null)
                throw new NullReferenceException();
            Contract.EndContractBlock();

            return Activator.CreateInstanceFrom(assemblyFile,
                                                typeName);
        }

        [System.Security.SecurityCritical]  // auto-generated
        internal ObjectHandle InternalCreateInstanceFromWithNoSecurity (string assemblyName, string typeName) {
            PermissionSet.s_fullTrust.Assert();
            return CreateInstanceFrom(assemblyName, typeName);
        }

#if FEATURE_COMINTEROP
        // The first parameter should be named assemblyFile, but it was incorrectly named in a previous 
        //  release, and the compatibility police won't let us change the name now.
        public ObjectHandle CreateComInstanceFrom(String assemblyName,
                                                  String typeName)
                                         
        {
            if (this == null)
                throw new NullReferenceException();
            Contract.EndContractBlock();

            return Activator.CreateComInstanceFrom(assemblyName,
                                                   typeName);
        }

        public ObjectHandle CreateComInstanceFrom(String assemblyFile,
                                                  String typeName,
                                                  byte[] hashValue, 
                                                  AssemblyHashAlgorithm hashAlgorithm)
                                         
        {
            if (this == null)
                throw new NullReferenceException();
            Contract.EndContractBlock();

            return Activator.CreateComInstanceFrom(assemblyFile,
                                                   typeName,
                                                   hashValue, 
                                                   hashAlgorithm);
        }

#endif // FEATURE_COMINTEROP

        public ObjectHandle CreateInstance(String assemblyName,
                                           String typeName,
                                           Object[] activationAttributes)
                                         
        {
            // jit does not check for that, so we should do it ...
            if (this == null)
                throw new NullReferenceException();

            if (assemblyName == null)
                throw new ArgumentNullException(nameof(assemblyName));
            Contract.EndContractBlock();

            return Activator.CreateInstance(assemblyName,
                                            typeName,
                                            activationAttributes);
        }
                                  
        public ObjectHandle CreateInstanceFrom(String assemblyFile,
                                               String typeName,
                                               Object[] activationAttributes)
                                               
        {
            // jit does not check for that, so we should do it ...
            if (this == null)
                throw new NullReferenceException();
            Contract.EndContractBlock();

            return Activator.CreateInstanceFrom(assemblyFile,
                                                typeName,
                                                activationAttributes);
        }
                                         
        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of CreateInstance which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public ObjectHandle CreateInstance(String assemblyName, 
                                           String typeName, 
                                           bool ignoreCase,
                                           BindingFlags bindingAttr, 
                                           Binder binder,
                                           Object[] args,
                                           CultureInfo culture,
                                           Object[] activationAttributes,
                                           Evidence securityAttributes)
        {
            // jit does not check for that, so we should do it ...
            if (this == null)
                throw new NullReferenceException();
            
            if (assemblyName == null)
                throw new ArgumentNullException(nameof(assemblyName));
            Contract.EndContractBlock();

#pragma warning disable 618
            return Activator.CreateInstance(assemblyName,
                                            typeName,
                                            ignoreCase,
                                            bindingAttr,
                                            binder,
                                            args,
                                            culture,
                                            activationAttributes,
                                            securityAttributes);
#pragma warning restore 618
        }

        public ObjectHandle CreateInstance(string assemblyName,
                                           string typeName,
                                           bool ignoreCase,
                                           BindingFlags bindingAttr,
                                           Binder binder,
                                           object[] args,
                                           CultureInfo culture,
                                           object[] activationAttributes)
        {
            // jit does not check for that, so we should do it ...
            if (this == null)
                throw new NullReferenceException();

            if (assemblyName == null)
                throw new ArgumentNullException(nameof(assemblyName));
            Contract.EndContractBlock();

            return Activator.CreateInstance(assemblyName,
                                            typeName,
                                            ignoreCase,
                                            bindingAttr,
                                            binder,
                                            args,
                                            culture,
                                            activationAttributes);
        }

        [System.Security.SecurityCritical]  // auto-generated
        internal ObjectHandle InternalCreateInstanceWithNoSecurity (string assemblyName, 
                                                                    string typeName,
                                                                    bool ignoreCase,
                                                                    BindingFlags bindingAttr,
                                                                    Binder binder,
                                                                    Object[] args,
                                                                    CultureInfo culture,
                                                                    Object[] activationAttributes,
                                                                    Evidence securityAttributes)
        {
            PermissionSet.s_fullTrust.Assert();
#pragma warning disable 618
            return CreateInstance(assemblyName, typeName, ignoreCase, bindingAttr, binder, args, culture, activationAttributes, securityAttributes);
#pragma warning restore 618
        }

        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of CreateInstanceFrom which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public ObjectHandle CreateInstanceFrom(String assemblyFile,
                                               String typeName, 
                                               bool ignoreCase,
                                               BindingFlags bindingAttr, 
                                               Binder binder,
                                               Object[] args,
                                               CultureInfo culture,
                                               Object[] activationAttributes,
                                               Evidence securityAttributes)

        {
            // jit does not check for that, so we should do it ...
            if (this == null)
                throw new NullReferenceException();
            Contract.EndContractBlock();

            return Activator.CreateInstanceFrom(assemblyFile,
                                                typeName,
                                                ignoreCase,
                                                bindingAttr,
                                                binder,
                                                args,
                                                culture,
                                                activationAttributes,
                                                securityAttributes);
        }

        public ObjectHandle CreateInstanceFrom(string assemblyFile,
                                               string typeName,
                                               bool ignoreCase,
                                               BindingFlags bindingAttr,
                                               Binder binder,
                                               object[] args,
                                               CultureInfo culture,
                                               object[] activationAttributes)
        {
            // jit does not check for that, so we should do it ...
            if (this == null)
                throw new NullReferenceException();
            Contract.EndContractBlock();

            return Activator.CreateInstanceFrom(assemblyFile,
                                                typeName,
                                                ignoreCase,
                                                bindingAttr,
                                                binder,
                                                args,
                                                culture,
                                                activationAttributes);
        }

        [System.Security.SecurityCritical]  // auto-generated
        internal ObjectHandle InternalCreateInstanceFromWithNoSecurity (string assemblyName, 
                                                                        string typeName,
                                                                        bool ignoreCase,
                                                                        BindingFlags bindingAttr,
                                                                        Binder binder,
                                                                        Object[] args,
                                                                        CultureInfo culture,
                                                                        Object[] activationAttributes,
                                                                        Evidence securityAttributes)
        {
            PermissionSet.s_fullTrust.Assert();
#pragma warning disable 618
            return CreateInstanceFrom(assemblyName, typeName, ignoreCase, bindingAttr, binder, args, culture, activationAttributes, securityAttributes);
#pragma warning restore 618
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public Assembly Load(AssemblyName assemblyRef)
        {
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return RuntimeAssembly.InternalLoadAssemblyName(assemblyRef, null, null, ref stackMark, true /*thrownOnFileNotFound*/, false, false);
        }
        
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public Assembly Load(String assemblyString)
        {
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return RuntimeAssembly.InternalLoad(assemblyString, null, ref stackMark, false);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public Assembly Load(byte[] rawAssembly)
        {
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return RuntimeAssembly.nLoadImage(rawAssembly,
                                       null, // symbol store
                                       null, // evidence
                                       ref stackMark,
                                       false,
                                       SecurityContextSource.CurrentAssembly);

        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        public Assembly Load(byte[] rawAssembly,
                             byte[] rawSymbolStore)
        {
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return RuntimeAssembly.nLoadImage(rawAssembly,
                                       rawSymbolStore,
                                       null, // evidence
                                       ref stackMark,
                                       false, // fIntrospection
                                       SecurityContextSource.CurrentAssembly);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
#pragma warning disable 618
        [SecurityPermissionAttribute(SecurityAction.Demand, ControlEvidence = true)]
#pragma warning restore 618
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of Load which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkId=155570 for more information.")]
        public Assembly Load(byte[] rawAssembly,
                             byte[] rawSymbolStore,
                             Evidence securityEvidence)
        {
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return RuntimeAssembly.nLoadImage(rawAssembly,
                                       rawSymbolStore,
                                       securityEvidence,
                                       ref stackMark,
                                       false, // fIntrospection
                                       SecurityContextSource.CurrentAssembly);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of Load which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public Assembly Load(AssemblyName assemblyRef,
                             Evidence assemblySecurity)
        {
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return RuntimeAssembly.InternalLoadAssemblyName(assemblyRef, assemblySecurity, null, ref stackMark, true /*thrownOnFileNotFound*/, false, false);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)] // Methods containing StackCrawlMark local var has to be marked non-inlineable
        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of Load which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public Assembly Load(String assemblyString,
                             Evidence assemblySecurity)
        {
            StackCrawlMark stackMark = StackCrawlMark.LookForMyCaller;
            return RuntimeAssembly.InternalLoad(assemblyString, assemblySecurity, ref stackMark, false);
        }

        public int ExecuteAssembly(String assemblyFile)
        {
            return ExecuteAssembly(assemblyFile, (string[])null);
        }

        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of ExecuteAssembly which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public int ExecuteAssembly(String assemblyFile,
                                   Evidence assemblySecurity)
        {
            return ExecuteAssembly(assemblyFile, assemblySecurity, null);
        }
    
        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of ExecuteAssembly which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public int ExecuteAssembly(String assemblyFile,
                                   Evidence assemblySecurity,
                                   String[] args)
        {
            RuntimeAssembly assembly = (RuntimeAssembly)Assembly.LoadFrom(assemblyFile, assemblySecurity);

            if (args == null)
                args = new String[0];

            return nExecuteAssembly(assembly, args);
        }

        public int ExecuteAssembly(string assemblyFile, string[] args)
        {
            RuntimeAssembly assembly = (RuntimeAssembly)Assembly.LoadFrom(assemblyFile);

            if (args == null)
                args = new String[0];

            return nExecuteAssembly(assembly, args);
        }

        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of ExecuteAssembly which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public int ExecuteAssembly(String assemblyFile,
                                   Evidence assemblySecurity,
                                   String[] args,
                                   byte[] hashValue, 
                                   AssemblyHashAlgorithm hashAlgorithm)
        {
            RuntimeAssembly assembly = (RuntimeAssembly)Assembly.LoadFrom(assemblyFile, 
                                                                          assemblySecurity,
                                                                          hashValue,
                                                                          hashAlgorithm);
            if (args == null)
                args = new String[0];

            return nExecuteAssembly(assembly, args);
        }

        public int ExecuteAssembly(string assemblyFile,
                                   string[] args,
                                   byte[] hashValue,
                                   AssemblyHashAlgorithm hashAlgorithm)
        {
            RuntimeAssembly assembly = (RuntimeAssembly)Assembly.LoadFrom(assemblyFile,
                                                                          hashValue,
                                                                          hashAlgorithm);
            if (args == null)
                args = new String[0];

            return nExecuteAssembly(assembly, args);
        }

        [System.Security.SecurityCritical] // auto-generated
        public int ExecuteAssemblyByName(String assemblyName)
        {
            return ExecuteAssemblyByName(assemblyName, (string[])null);
        }

        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of ExecuteAssemblyByName which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public int ExecuteAssemblyByName(String assemblyName,
                                         Evidence assemblySecurity)
        {
#pragma warning disable 618
            return ExecuteAssemblyByName(assemblyName, assemblySecurity, null);
#pragma warning restore 618
        }

        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of ExecuteAssemblyByName which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public int ExecuteAssemblyByName(String assemblyName,
                                         Evidence assemblySecurity,
                                         params String[] args)
        {
            RuntimeAssembly assembly = (RuntimeAssembly)Assembly.Load(assemblyName, assemblySecurity);

            if (args == null)
                args = new String[0];

            return nExecuteAssembly(assembly, args);
        }

        public int ExecuteAssemblyByName(string assemblyName, params string[] args)
        {
            RuntimeAssembly assembly = (RuntimeAssembly)Assembly.Load(assemblyName);

            if (args == null)
                args = new String[0];

            return nExecuteAssembly(assembly, args);
        }

        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of ExecuteAssemblyByName which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public int ExecuteAssemblyByName(AssemblyName assemblyName,
                                         Evidence assemblySecurity,
                                         params String[] args)
        {
            RuntimeAssembly assembly = (RuntimeAssembly)Assembly.Load(assemblyName, assemblySecurity);

            if (args == null)
                args = new String[0];

            return nExecuteAssembly(assembly, args);
        }

        public int ExecuteAssemblyByName(AssemblyName assemblyName, params string[] args)
        {
            RuntimeAssembly assembly = (RuntimeAssembly)Assembly.Load(assemblyName);

            if (args == null)
                args = new String[0];

            return nExecuteAssembly(assembly, args);
        }

        public static AppDomain CurrentDomain
        {
            get {
                Contract.Ensures(Contract.Result<AppDomain>() != null);
                return Thread.GetDomain();
            }
        }

        public String FriendlyName
        {
            [System.Security.SecuritySafeCritical]  // auto-generated
            get { return nGetFriendlyName(); }
        } 

        public String BaseDirectory
        {
            [System.Security.SecurityCritical]
            get {
                return FusionStore.ApplicationBase;
            }
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        public override String ToString()
        {
            StringBuilder sb = StringBuilderCache.Acquire();

            String fn = nGetFriendlyName();
            if (fn != null) {
                sb.Append(Environment.GetResourceString("Loader_Name") + fn);
                sb.Append(Environment.NewLine);
            }

            if(_Policies == null || _Policies.Length == 0) 
                sb.Append(Environment.GetResourceString("Loader_NoContextPolicies")
                          + Environment.NewLine);
            else {
                sb.Append(Environment.GetResourceString("Loader_ContextPolicies")
                          + Environment.NewLine);
                for(int i = 0;i < _Policies.Length; i++) {
                    sb.Append(_Policies[i]);
                    sb.Append(Environment.NewLine);
                }
            }
    
            return StringBuilderCache.GetStringAndRelease(sb);
        }
        
        public Assembly[] GetAssemblies()
        {
            return nGetAssemblies(false /* forIntrospection */);
        }

        public Assembly[] ReflectionOnlyGetAssemblies()
        {
            return nGetAssemblies(true /* forIntrospection */);
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern Assembly[] nGetAssemblies(bool forIntrospection);

        // this is true when we've removed the handles etc so really can't do anything
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern bool IsUnloadingForcedFinalize();

        // this is true when we've just started going through the finalizers and are forcing objects to finalize
        // so must be aware that certain infrastructure may have gone away
        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        public extern bool IsFinalizingForUnload();

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal static extern void PublishAnonymouslyHostedDynamicMethodsAssembly(RuntimeAssembly assemblyHandle);

        [System.Security.SecurityCritical]  // auto-generated_required
        public void SetData (string name, object data) {
            SetDataHelper(name, data, null);
        }

        [System.Security.SecurityCritical]  // auto-generated_required
        public void SetData (string name, object data, IPermission permission)
        {
            if (!name.Equals("LOCATION_URI"))
            {
                // Only LOCATION_URI can be set using AppDomain.SetData
                throw new InvalidOperationException(Environment.GetResourceString("InvalidOperation_SetData_OnlyLocationURI", name));
            }

            SetDataHelper(name, data, permission);
        }

        [System.Security.SecurityCritical]  // auto-generated
        private void SetDataHelper (string name, object data, IPermission permission)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            Contract.EndContractBlock();

            // SetData should only be used to set values that don't already exist.
            object[] currentVal;
            lock (((ICollection)LocalStore).SyncRoot) {
                LocalStore.TryGetValue(name, out currentVal);
            }
            if (currentVal != null && currentVal[0] != null)
            {
                throw new InvalidOperationException(Environment.GetResourceString("InvalidOperation_SetData_OnlyOnce"));
            }

            lock (((ICollection)LocalStore).SyncRoot) {
                LocalStore[name] = new object[] {data, permission};
            }
        }

        [Pure]
        [System.Security.SecurityCritical] // auto-generated
        public Object GetData(string name)
        {
            if(name == null)
                throw new ArgumentNullException(nameof(name));
            Contract.EndContractBlock();

            int key = AppDomainSetup.Locate(name);
            if(key == -1) 
            {
#if FEATURE_LOADER_OPTIMIZATION
                if(name.Equals(AppDomainSetup.LoaderOptimizationKey))
                    return FusionStore.LoaderOptimization;
                else 
#endif // FEATURE_LOADER_OPTIMIZATION                    
                {
                    object[] data;
                    lock (((ICollection)LocalStore).SyncRoot) {
                        LocalStore.TryGetValue(name, out data);
                    }
                    if (data == null)
                        return null;
                    if (data[1] != null) {
                        IPermission permission = (IPermission) data[1];
                        permission.Demand();
                    }
                    return data[0];
                }
            }
           else {
                // Be sure to call these properties, not Value, so
                // that the appropriate permission demand will be done
                switch(key) {
                case (int) AppDomainSetup.LoaderInformation.ApplicationBaseValue:
                    return FusionStore.ApplicationBase;
                case (int) AppDomainSetup.LoaderInformation.ApplicationNameValue:
                    return FusionStore.ApplicationName;
                default:
                    Contract.Assert(false, "Need to handle new LoaderInformation value in AppDomain.GetData()");
                    return null;
                }
            }
        }

        // The compat flags are set at domain creation time to indicate that the given breaking
        // change should not be used in this domain.
        //
        // After the domain has been created, this Nullable boolean returned by this method should
        // always have a value.  Code in the runtime uses this to know if it is safe to cache values
        // that might change if the compatibility switches have not been set yet.    
        public Nullable<bool> IsCompatibilitySwitchSet(String value)
        {
            Nullable<bool> fReturn;

            if (_compatFlagsInitialized == false) 
            {
                fReturn = new Nullable<bool>();
            } 
            else
            {
                fReturn  = new Nullable<bool>(_compatFlags != null && _compatFlags.ContainsKey(value));
            }

            return fReturn;
        }
        
        [Obsolete("AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202", false)]
        [DllImport(Microsoft.Win32.Win32Native.KERNEL32)]
        public static extern int GetCurrentThreadId();

        internal ApplicationTrust ApplicationTrust
        {
            get {
                if (_applicationTrust == null && _IsFastFullTrustDomain) {
                    _applicationTrust = new ApplicationTrust(new PermissionSet(PermissionState.Unrestricted));
                }

                return _applicationTrust;
            }
        }

        public String DynamicDirectory
        {
            [System.Security.SecuritySafeCritical]  // auto-generated
            get {
                String dyndir = GetDynamicDir();
                if (dyndir != null)
                    new FileIOPermission( FileIOPermissionAccess.PathDiscovery, dyndir ).Demand();

                return dyndir;
            }
        }

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        extern private String GetDynamicDir();

        private AppDomain() {
            throw new NotSupportedException(Environment.GetResourceString(ResId.NotSupported_Constructor));
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern int _nExecuteAssembly(RuntimeAssembly assembly, String[] args);
        internal int nExecuteAssembly(RuntimeAssembly assembly, String[] args)
        {
            return _nExecuteAssembly(assembly, args);
        }

#if FEATURE_VERSIONING
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern void nCreateContext();

        [System.Security.SecurityCritical]
        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        [SuppressUnmanagedCodeSecurity]
        private static extern void nSetupBindingPaths(String trustedPlatformAssemblies, String platformResourceRoots, String appPath, String appNiPaths, String appLocalWinMD);

        #if FEATURE_CORECLR
        [System.Security.SecurityCritical] // auto-generated
        #endif
        internal void SetupBindingPaths(String trustedPlatformAssemblies, String platformResourceRoots, String appPath, String appNiPaths, String appLocalWinMD)
        {
            nSetupBindingPaths(trustedPlatformAssemblies, platformResourceRoots, appPath, appNiPaths, appLocalWinMD);
        }
#endif // FEATURE_VERSIONING

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern String nGetFriendlyName();
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern bool nIsDefaultAppDomainForEvidence();

        // support reliability for certain event handlers, if the target
        // methods also participate in this discipline.  If caller passes
        // an existing MulticastDelegate, then we could use a MDA to indicate
        // that reliability is not guaranteed.  But if it is a single cast
        // scenario, we can make it work.

        public event EventHandler ProcessExit
        {
            [System.Security.SecuritySafeCritical]  // auto-generated_required
            add
            {
                if (value != null)
                {
                    RuntimeHelpers.PrepareContractedDelegate(value);
                    lock(this)
                        _processExit += value;
                }
            }
            remove
            {
                lock(this)
                    _processExit -= value;
            }
        }


        public event EventHandler DomainUnload
        {
            [System.Security.SecuritySafeCritical]  // auto-generated_required
            add
            {
                if (value != null)
                {
                    RuntimeHelpers.PrepareContractedDelegate(value);
                    lock(this)
                        _domainUnload += value;
                }
            }
            [System.Security.SecuritySafeCritical] 
            remove
            {
                lock(this)
                    _domainUnload -= value;
            }
        }


        public event UnhandledExceptionEventHandler UnhandledException
        {
            [System.Security.SecurityCritical]  // auto-generated_required
            add
            {
                if (value != null)
                {
                    RuntimeHelpers.PrepareContractedDelegate(value);
                    lock(this)
                        _unhandledException += value;
                }
            }
            [System.Security.SecurityCritical]  // auto-generated_required
            remove
            {
                lock(this)
                    _unhandledException -= value;
            }
        }

#if FEATURE_EXCEPTION_NOTIFICATIONS
        // This is the event managed code can wireup against to be notified
        // about first chance exceptions. 
        //
        // To register/unregister the callback, the code must be SecurityCritical.
        public event EventHandler<FirstChanceExceptionEventArgs> FirstChanceException
        {
            [System.Security.SecurityCritical]  // auto-generated_required
            add
            {
                if (value != null)
                {
                    RuntimeHelpers.PrepareContractedDelegate(value);
                    lock(this)
                        _firstChanceException += value;
                }
            }
            [System.Security.SecurityCritical]  // auto-generated_required
            remove
            {
                lock(this)
                    _firstChanceException -= value;
            }
        }
#endif // FEATURE_EXCEPTION_NOTIFICATIONS

        private void OnAssemblyLoadEvent(RuntimeAssembly LoadedAssembly)
        {
            AssemblyLoadEventHandler eventHandler = AssemblyLoad;
            if (eventHandler != null) {
                AssemblyLoadEventArgs ea = new AssemblyLoadEventArgs(LoadedAssembly);
                eventHandler(this, ea);
            }
        }
    
        // This method is called by the VM.
        [System.Security.SecurityCritical]
        private RuntimeAssembly OnResourceResolveEvent(RuntimeAssembly assembly, String resourceName)
        {
            ResolveEventHandler eventHandler = _ResourceResolve;
            if ( eventHandler == null)
                return null;

            Delegate[] ds = eventHandler.GetInvocationList();
            int len = ds.Length;
            for (int i = 0; i < len; i++) {
                Assembly asm = ((ResolveEventHandler)ds[i])(this, new ResolveEventArgs(resourceName, assembly));
                RuntimeAssembly ret = GetRuntimeAssembly(asm);
                if (ret != null)
                    return ret;
            }

            return null;
        }
        
        // This method is called by the VM
        [System.Security.SecurityCritical]
        private RuntimeAssembly OnTypeResolveEvent(RuntimeAssembly assembly, String typeName)
        {
            ResolveEventHandler eventHandler = _TypeResolve;
            if (eventHandler == null)
                return null;

            Delegate[] ds = eventHandler.GetInvocationList();
            int len = ds.Length;
            for (int i = 0; i < len; i++) {
                Assembly asm = ((ResolveEventHandler)ds[i])(this, new ResolveEventArgs(typeName, assembly));
                RuntimeAssembly ret = GetRuntimeAssembly(asm);
                if (ret != null)
                    return ret;
            }

            return null;
        }

        // This method is called by the VM.
        [System.Security.SecurityCritical]
        private RuntimeAssembly OnAssemblyResolveEvent(RuntimeAssembly assembly, String assemblyFullName)
        {
            ResolveEventHandler eventHandler = _AssemblyResolve;

            if (eventHandler == null)
            {
                return null;
            }

            Delegate[] ds = eventHandler.GetInvocationList();
            int len = ds.Length;
            for (int i = 0; i < len; i++) {
                Assembly asm = ((ResolveEventHandler)ds[i])(this, new ResolveEventArgs(assemblyFullName, assembly));
                RuntimeAssembly ret = GetRuntimeAssembly(asm);
                if (ret != null)
                    return ret;
            }
            
            return null;
        }

#if FEATURE_COMINTEROP
        // Called by VM - code:CLRPrivTypeCacheWinRT::RaiseDesignerNamespaceResolveEvent
        private string[] OnDesignerNamespaceResolveEvent(string namespaceName)
        {
            return System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMetadata.OnDesignerNamespaceResolveEvent(this, namespaceName);
        }
#endif // FEATURE_COMINTEROP

        internal AppDomainSetup FusionStore
        {
            get {
                Contract.Assert(_FusionStore != null, 
                                "Fusion store has not been correctly setup in this domain");
                return _FusionStore;
            }
        }

        internal static RuntimeAssembly GetRuntimeAssembly(Assembly asm)
        {
            if (asm == null)
                return null;

            RuntimeAssembly rtAssembly = asm as RuntimeAssembly;
            if (rtAssembly != null)
                return rtAssembly;

            AssemblyBuilder ab = asm as AssemblyBuilder;
            if (ab != null)
                return ab.InternalAssembly;

            return null;
        }

        private Dictionary<String, Object[]> LocalStore
        {
            get { 
                if (_LocalStore != null)
                    return _LocalStore;
                else {
                    _LocalStore = new Dictionary<String, Object[]>();
                    return _LocalStore;
                }
            }
        }

        // Used to determine if server object context is valid in
        // x-domain remoting scenarios.
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        internal static extern bool IsDomainIdValid(Int32 id);

        [System.Security.SecurityCritical]
        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        [SuppressUnmanagedCodeSecurity]
        private static extern void nSetNativeDllSearchDirectories(string paths);

        [System.Security.SecurityCritical]  // auto-generated
        private void SetupFusionStore(AppDomainSetup info, AppDomainSetup oldInfo)
        {
            Contract.Requires(info != null);

            if (info.ApplicationBase == null)
            {
                info.SetupDefaults(RuntimeEnvironment.GetModuleFileName(), imageLocationAlreadyNormalized : true);
            }

#if FEATURE_VERSIONING
            nCreateContext();
#endif // FEATURE_VERSIONING

#if FEATURE_LOADER_OPTIMIZATION
            if (info.LoaderOptimization != LoaderOptimization.NotSpecified || (oldInfo != null && info.LoaderOptimization != oldInfo.LoaderOptimization))
                UpdateLoaderOptimization(info.LoaderOptimization);
#endif
            // This must be the last action taken
            _FusionStore = info;
        }

        // used to package up evidence, so it can be serialized
        //   for the call to InternalRemotelySetupRemoteDomain
        [Serializable]
        private class EvidenceCollection
        {
            public Evidence ProvidedSecurityInfo;
            public Evidence CreatorsSecurityInfo;
        }

        private static void RunInitializer(AppDomainSetup setup)
        {
            if (setup.AppDomainInitializer!=null)
            {
                string[] args=null;
                if (setup.AppDomainInitializerArguments!=null)
                    args=(string[])setup.AppDomainInitializerArguments.Clone();
                setup.AppDomainInitializer(args);
            }
        }

        // Used to switch into other AppDomain and call SetupRemoteDomain.
        //   We cannot simply call through the proxy, because if there
        //   are any remoting sinks registered, they can add non-mscorlib
        //   objects to the message (causing an assembly load exception when
        //   we try to deserialize it on the other side)
        [System.Security.SecurityCritical]  // auto-generated
        private static object PrepareDataForSetup(String friendlyName,
                                                        AppDomainSetup setup,
                                                        Evidence providedSecurityInfo,
                                                        Evidence creatorsSecurityInfo,
                                                        IntPtr parentSecurityDescriptor,
                                                        string sandboxName,
                                                        string[] propertyNames,
                                                        string[] propertyValues)
        {
            byte[] serializedEvidence = null;
            bool generateDefaultEvidence = false;

            AppDomainInitializerInfo initializerInfo = null;
            if (setup!=null && setup.AppDomainInitializer!=null)
                initializerInfo=new AppDomainInitializerInfo(setup.AppDomainInitializer);

            // will travel x-Ad, drop non-agile data 
            AppDomainSetup newSetup = new AppDomainSetup(setup, false);

            // Remove the special AppDomainCompatSwitch entries from the set of name value pairs
            // And add them to the AppDomainSetup
            //
            // This is only supported on CoreCLR through ICLRRuntimeHost2.CreateAppDomainWithManager
            // Desktop code should use System.AppDomain.CreateDomain() or 
            // System.AppDomainManager.CreateDomain() and add the flags to the AppDomainSetup
            List<String> compatList = new List<String>();

            if(propertyNames!=null && propertyValues != null)
            {
                for (int i=0; i<propertyNames.Length; i++)
                {
                    if(String.Compare(propertyNames[i], "AppDomainCompatSwitch", StringComparison.OrdinalIgnoreCase) == 0) 
                    {
                        compatList.Add(propertyValues[i]);
                        propertyNames[i] = null;
                        propertyValues[i] = null;
                    }

                }
                
                if (compatList.Count > 0)
                {
                    newSetup.SetCompatibilitySwitches(compatList);
                }
            }

            return new Object[] 
            {
                friendlyName, 
                newSetup, 
                parentSecurityDescriptor, 
                generateDefaultEvidence,
                serializedEvidence,
                initializerInfo,
                sandboxName,
                propertyNames,
                propertyValues
            };  
        } // PrepareDataForSetup

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.NoInlining)]
        private static Object Setup(Object arg)
        {
            Contract.Requires(arg != null && arg is Object[]);
            Contract.Requires(((Object[])arg).Length >= 8);

            Object[] args=(Object[])arg;
            String           friendlyName               = (String)args[0];
            AppDomainSetup   setup                      = (AppDomainSetup)args[1];
            IntPtr           parentSecurityDescriptor   = (IntPtr)args[2];
            bool             generateDefaultEvidence    = (bool)args[3];
            byte[]           serializedEvidence         = (byte[])args[4];
            AppDomainInitializerInfo initializerInfo    = (AppDomainInitializerInfo)args[5];
            string           sandboxName                = (string)args[6];
            string[]         propertyNames              = (string[])args[7]; // can contain null elements
            string[]         propertyValues             = (string[])args[8]; // can contain null elements
            // extract evidence
            Evidence providedSecurityInfo = null;
            Evidence creatorsSecurityInfo = null;

            AppDomain ad = AppDomain.CurrentDomain;
            AppDomainSetup newSetup=new AppDomainSetup(setup,false);

            if(propertyNames!=null && propertyValues != null)
            {
                for (int i = 0; i < propertyNames.Length; i++)
                {
                    // We want to set native dll probing directories before any P/Invokes have a
                    // chance to fire. The Path class, for one, has P/Invokes.
                    if (propertyNames[i] == "NATIVE_DLL_SEARCH_DIRECTORIES")
                    {
                        if (propertyValues[i] == null)
                            throw new ArgumentNullException("NATIVE_DLL_SEARCH_DIRECTORIES");

                        string paths = propertyValues[i];
                        if (paths.Length == 0)
                            break;

                        nSetNativeDllSearchDirectories(paths);
                    }
                }

                for (int i=0; i<propertyNames.Length; i++)
                {
                    if(propertyNames[i]=="APPBASE") // make sure in sync with Fusion
                    {
                        if(propertyValues[i]==null)
                            throw new ArgumentNullException("APPBASE");

                        if (PathInternal.IsPartiallyQualified(propertyValues[i]))
                            throw new ArgumentException( Environment.GetResourceString( "Argument_AbsolutePathRequired" ) );

                        newSetup.ApplicationBase = NormalizePath(propertyValues[i], fullCheck: true);
                    }
#if FEATURE_LOADER_OPTIMIZATION
                    else if(propertyNames[i]=="LOADER_OPTIMIZATION")
                    {
                        if(propertyValues[i]==null)
                            throw new ArgumentNullException("LOADER_OPTIMIZATION");

                        switch(propertyValues[i])
                        {
                            case "SingleDomain": newSetup.LoaderOptimization=LoaderOptimization.SingleDomain;break;
                            case "MultiDomain": newSetup.LoaderOptimization=LoaderOptimization.MultiDomain;break;
                            case "MultiDomainHost": newSetup.LoaderOptimization=LoaderOptimization.MultiDomainHost;break;
                            case "NotSpecified": newSetup.LoaderOptimization=LoaderOptimization.NotSpecified;break;
                            default: throw new ArgumentException(Environment.GetResourceString("Argument_UnrecognizedLoaderOptimization"), "LOADER_OPTIMIZATION");
                        }
                    }
#endif // FEATURE_LOADER_OPTIMIZATION
                    else if(propertyNames[i]=="TRUSTED_PLATFORM_ASSEMBLIES" ||
                       propertyNames[i]=="PLATFORM_RESOURCE_ROOTS" ||
                       propertyNames[i]=="APP_PATHS" ||
                       propertyNames[i]=="APP_NI_PATHS")
                    {
                        string values = propertyValues[i];
                        if(values == null)
                            throw new ArgumentNullException(propertyNames[i]);

                        ad.SetDataHelper(propertyNames[i], NormalizeAppPaths(values), null);
                    }
                    else if(propertyNames[i]!= null)
                    {
                        ad.SetDataHelper(propertyNames[i],propertyValues[i],null);     // just propagate
                    }
                }
            }

            ad.SetupFusionStore(newSetup, null); // makes FusionStore a ref to newSetup
            
            // technically, we don't need this, newSetup refers to the same object as FusionStore 
            // but it's confusing since it isn't immediately obvious whether we have a ref or a copy
            AppDomainSetup adSetup = ad.FusionStore; 

            adSetup.InternalSetApplicationTrust(sandboxName);

            // set up the friendly name
            ad.nSetupFriendlyName(friendlyName);

#if FEATURE_COMINTEROP
            if (setup != null && setup.SandboxInterop)
            {
                ad.nSetDisableInterfaceCache();
            }
#endif // FEATURE_COMINTEROP

            // set up the AppDomainManager for this domain and initialize security.
            if (adSetup.AppDomainManagerAssembly != null && adSetup.AppDomainManagerType != null)
            {
                ad.SetAppDomainManagerType(adSetup.AppDomainManagerAssembly, adSetup.AppDomainManagerType);
            }

            ad.CreateAppDomainManager(); // could modify FusionStore's object
            ad.InitializeDomainSecurity(providedSecurityInfo,
                                        creatorsSecurityInfo,
                                        generateDefaultEvidence,
                                        parentSecurityDescriptor,
                                        true);
            
            // can load user code now
            if(initializerInfo!=null)
                adSetup.AppDomainInitializer=initializerInfo.Unwrap();
            RunInitializer(adSetup);

            return null;
        }

        private static string NormalizeAppPaths(string values)
        {
            int estimatedLength = values.Length + 1; // +1 for extra separator temporarily added at end
            StringBuilder sb = StringBuilderCache.Acquire(estimatedLength);

            for (int pos = 0; pos < values.Length; pos++)
            {
                string path;

                int nextPos = values.IndexOf(Path.PathSeparator, pos);
                if (nextPos == -1)
                {
                    path = values.Substring(pos);
                    pos = values.Length - 1;
                }
                else
                {
                    path = values.Substring(pos, nextPos - pos);
                    pos = nextPos;
                }

                // Skip empty directories
                if (path.Length == 0)
                    continue;

                if (PathInternal.IsPartiallyQualified(path))
                    throw new ArgumentException(Environment.GetResourceString("Argument_AbsolutePathRequired"));

                string appPath = NormalizePath(path, fullCheck: true);
                sb.Append(appPath);
                sb.Append(Path.PathSeparator);
            }

            // Strip the last separator
            if (sb.Length > 0)
            {
                sb.Remove(sb.Length - 1, 1);
            }

            return StringBuilderCache.GetStringAndRelease(sb);
        }

        [SecuritySafeCritical]
        internal static string NormalizePath(string path, bool fullCheck)
        {
            return Path.GetFullPath(path);
        }

        // This routine is called from unmanaged code to
        // set the default fusion context.
        [System.Security.SecurityCritical]  // auto-generated
        private void SetupDomain(bool allowRedirects, String path, String configFile, String[] propertyNames, String[] propertyValues)
        {
            // It is possible that we could have multiple threads initializing
            // the default domain. We will just take the winner of these two.
            // (eg. one thread doing a com call and another doing attach for IJW)
            lock (this)
            {
                if(_FusionStore == null)
                {
                    AppDomainSetup setup = new AppDomainSetup();

                    // always use internet permission set
                    setup.InternalSetApplicationTrust("Internet");
                    SetupFusionStore(setup, null);
                }
            }
        }

#if FEATURE_LOADER_OPTIMIZATION
       [System.Security.SecurityCritical]  // auto-generated
       private void SetupLoaderOptimization(LoaderOptimization policy)
        {
            if(policy != LoaderOptimization.NotSpecified) {
                Contract.Assert(FusionStore.LoaderOptimization == LoaderOptimization.NotSpecified,
                                "It is illegal to change the Loader optimization on a domain");

                FusionStore.LoaderOptimization = policy;
                UpdateLoaderOptimization(FusionStore.LoaderOptimization);
            }
        }
#endif

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr GetSecurityDescriptor();

        [SecurityCritical]
        private void SetupDomainSecurity(Evidence appDomainEvidence,
                                         IntPtr creatorsSecurityDescriptor,
                                         bool publishAppDomain)
        {
            Evidence stackEvidence = appDomainEvidence;
            SetupDomainSecurity(GetNativeHandle(),
                                JitHelpers.GetObjectHandleOnStack(ref stackEvidence),
                                creatorsSecurityDescriptor,
                                publishAppDomain);

        }

        [SecurityCritical]
        [SuppressUnmanagedCodeSecurity]
        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        private static extern void SetupDomainSecurity(AppDomainHandle appDomain,
                                                       ObjectHandleOnStack appDomainEvidence,
                                                       IntPtr creatorsSecurityDescriptor,
                                                       [MarshalAs(UnmanagedType.Bool)] bool publishAppDomain);

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern void nSetupFriendlyName(string friendlyName);

#if FEATURE_COMINTEROP
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern void nSetDisableInterfaceCache();
#endif // FEATURE_COMINTEROP

#if FEATURE_LOADER_OPTIMIZATION
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern void UpdateLoaderOptimization(LoaderOptimization optimization);
#endif

        public AppDomainSetup SetupInformation
        {
            get {
                return new AppDomainSetup(FusionStore,true);
            }
        }

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern String IsStringInterned(String str);

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern String GetOrInternString(String str);
        
        [SecurityCritical]
        [SuppressUnmanagedCodeSecurity]
        [DllImport(JitHelpers.QCall, CharSet = CharSet.Unicode)]
        private static extern void GetGrantSet(AppDomainHandle domain, ObjectHandleOnStack retGrantSet);

        public PermissionSet PermissionSet
        {
            // SecurityCritical because permissions can contain sensitive information such as paths
            [SecurityCritical]
            get
            {
                PermissionSet grantSet = null;
                GetGrantSet(GetNativeHandle(), JitHelpers.GetObjectHandleOnStack(ref grantSet));

                if (grantSet != null)
                {
                    return grantSet.Copy();
                }
                else
                {
                    return new PermissionSet(PermissionState.Unrestricted);
                }
            }
        }

        public bool IsFullyTrusted
        {
            [SecuritySafeCritical]
            get
            {
                PermissionSet grantSet = null;
                GetGrantSet(GetNativeHandle(), JitHelpers.GetObjectHandleOnStack(ref grantSet));

                return grantSet == null || grantSet.IsUnrestricted();
            }
        }

        public bool IsHomogenous
        {
            get
            {
                // Homogenous AppDomains always have an ApplicationTrust associated with them
                return _IsFastFullTrustDomain || _applicationTrust != null;
            }
        }

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern void nChangeSecurityPolicy();

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        [ReliabilityContract(Consistency.MayCorruptAppDomain, Cer.MayFail)]
        internal static extern void nUnload(Int32 domainInternal);
           
        public Object CreateInstanceAndUnwrap(String assemblyName,
                                              String typeName)
        {
            ObjectHandle oh = CreateInstance(assemblyName, typeName);
            if (oh == null)
                return null;

            return oh.Unwrap();
        } // CreateInstanceAndUnwrap

        public Object CreateInstanceAndUnwrap(String assemblyName, 
                                              String typeName,
                                              Object[] activationAttributes)
        {
            ObjectHandle oh = CreateInstance(assemblyName, typeName, activationAttributes);
            if (oh == null)
                return null; 

            return oh.Unwrap();
        } // CreateInstanceAndUnwrap


        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of CreateInstanceAndUnwrap which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public Object CreateInstanceAndUnwrap(String assemblyName, 
                                              String typeName, 
                                              bool ignoreCase,
                                              BindingFlags bindingAttr, 
                                              Binder binder,
                                              Object[] args,
                                              CultureInfo culture,
                                              Object[] activationAttributes,
                                              Evidence securityAttributes)
        {
#pragma warning disable 618
            ObjectHandle oh = CreateInstance(assemblyName, typeName, ignoreCase, bindingAttr,
                binder, args, culture, activationAttributes, securityAttributes);
#pragma warning restore 618

            if (oh == null)
                return null; 
            
            return oh.Unwrap();
        } // CreateInstanceAndUnwrap

        public object CreateInstanceAndUnwrap(string assemblyName,
                                              string typeName,
                                              bool ignoreCase,
                                              BindingFlags bindingAttr,
                                              Binder binder,
                                              object[] args,
                                              CultureInfo culture,
                                              object[] activationAttributes)
        {
            ObjectHandle oh = CreateInstance(assemblyName,
                                             typeName,
                                             ignoreCase,
                                             bindingAttr,
                                             binder,
                                             args,
                                             culture,
                                             activationAttributes);

            if (oh == null)
            {
                return null;
            }

            return oh.Unwrap();
        }

        // The first parameter should be named assemblyFile, but it was incorrectly named in a previous 
        //  release, and the compatibility police won't let us change the name now.
        public Object CreateInstanceFromAndUnwrap(String assemblyName,
                                                  String typeName)
        {
            ObjectHandle oh = CreateInstanceFrom(assemblyName, typeName);
            if (oh == null)
                return null;  

            return oh.Unwrap();                
        } // CreateInstanceAndUnwrap


        // The first parameter should be named assemblyFile, but it was incorrectly named in a previous 
        //  release, and the compatibility police won't let us change the name now.
        public Object CreateInstanceFromAndUnwrap(String assemblyName,
                                                  String typeName,
                                                  Object[] activationAttributes)
        {
            ObjectHandle oh = CreateInstanceFrom(assemblyName, typeName, activationAttributes);
            if (oh == null)
                return null; 

            return oh.Unwrap();
        } // CreateInstanceAndUnwrap


        // The first parameter should be named assemblyFile, but it was incorrectly named in a previous 
        //  release, and the compatibility police won't let us change the name now.
        [Obsolete("Methods which use evidence to sandbox are obsolete and will be removed in a future release of the .NET Framework. Please use an overload of CreateInstanceFromAndUnwrap which does not take an Evidence parameter. See http://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        public Object CreateInstanceFromAndUnwrap(String assemblyName, 
                                                  String typeName, 
                                                  bool ignoreCase,
                                                  BindingFlags bindingAttr, 
                                                  Binder binder,
                                                  Object[] args,
                                                  CultureInfo culture,
                                                  Object[] activationAttributes,
                                                  Evidence securityAttributes)
        {
#pragma warning disable 618
            ObjectHandle oh = CreateInstanceFrom(assemblyName, typeName, ignoreCase, bindingAttr,
                binder, args, culture, activationAttributes, securityAttributes);
#pragma warning restore 618

            if (oh == null)
                return null; 

            return oh.Unwrap();
        } // CreateInstanceAndUnwrap

        public object CreateInstanceFromAndUnwrap(string assemblyFile,
                                                  string typeName,
                                                  bool ignoreCase,
                                                  BindingFlags bindingAttr,
                                                  Binder binder,
                                                  object[] args,
                                                  CultureInfo culture,
                                                  object[] activationAttributes)
        {
            ObjectHandle oh = CreateInstanceFrom(assemblyFile,
                                                 typeName,
                                                 ignoreCase,
                                                 bindingAttr,
                                                 binder,
                                                 args,
                                                 culture,
                                                 activationAttributes);
            if (oh == null)
            {
                return null;
            }
            
            return oh.Unwrap();
        }

        public Int32 Id
        {
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]  
            get {
                return GetId();
            }
        }

        [System.Security.SecuritySafeCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]              
        internal extern Int32 GetId();
        
        internal const Int32 DefaultADID = 1;
        
        public bool IsDefaultAppDomain()
        {
            if (GetId()==DefaultADID)
                return true;
            return false;
        }

#if FEATURE_APPDOMAIN_RESOURCE_MONITORING
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private static extern void nEnableMonitoring();

        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private static extern bool nMonitoringIsEnabled();

        // return -1 if ARM is not supported.
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern Int64 nGetTotalProcessorTime();

        // return -1 if ARM is not supported.
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern Int64 nGetTotalAllocatedMemorySize();

        // return -1 if ARM is not supported.
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern Int64 nGetLastSurvivedMemorySize();

        // return -1 if ARM is not supported.
        [System.Security.SecurityCritical]  // auto-generated
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private static extern Int64 nGetLastSurvivedProcessMemorySize();

        public static bool MonitoringIsEnabled
        {
            [System.Security.SecurityCritical]
            get {
                return nMonitoringIsEnabled();
            }
            
            [System.Security.SecurityCritical]
            set {
                if (value == false)
                {
                    throw new ArgumentException(Environment.GetResourceString("Arg_MustBeTrue"));
                }
                else
                {
                    nEnableMonitoring();
                }
            }
        }

        // Gets the total processor time for this AppDomain.
        // Throws NotSupportedException if ARM is not enabled.
        public TimeSpan MonitoringTotalProcessorTime 
        {
            [System.Security.SecurityCritical]
            get {
                Int64 i64ProcessorTime = nGetTotalProcessorTime();
                if (i64ProcessorTime == -1)
                {
                    throw new InvalidOperationException(Environment.GetResourceString("InvalidOperation_WithoutARM"));
                }
                return new TimeSpan(i64ProcessorTime);
            }
        }

        // Gets the number of bytes allocated in this AppDomain since
        // the AppDomain was created.
        // Throws NotSupportedException if ARM is not enabled.
        public Int64 MonitoringTotalAllocatedMemorySize 
        {
            [System.Security.SecurityCritical]
            get {
                Int64 i64AllocatedMemory = nGetTotalAllocatedMemorySize();
                if (i64AllocatedMemory == -1)
                {
                    throw new InvalidOperationException(Environment.GetResourceString("InvalidOperation_WithoutARM"));
                }
                return i64AllocatedMemory;
            }
        }

        // Gets the number of bytes survived after the last collection
        // that are known to be held by this AppDomain. After a full 
        // collection this number is accurate and complete. After an 
        // ephemeral collection this number is potentially incomplete.
        // Throws NotSupportedException if ARM is not enabled.
        public Int64 MonitoringSurvivedMemorySize
        {
            [System.Security.SecurityCritical]
            get {
                Int64 i64LastSurvivedMemory = nGetLastSurvivedMemorySize();
                if (i64LastSurvivedMemory == -1)
                {
                    throw new InvalidOperationException(Environment.GetResourceString("InvalidOperation_WithoutARM"));
                }
                return i64LastSurvivedMemory;
            }
        }

        // Gets the total bytes survived from the last collection. After 
        // a full collection this number represents the number of the bytes 
        // being held live in managed heaps. (This number should be close 
        // to the number obtained from GC.GetTotalMemory for a full collection.)
        // After an ephemeral collection this number represents the number 
        // of bytes being held live in ephemeral generations.
        // Throws NotSupportedException if ARM is not enabled.
        public static Int64 MonitoringSurvivedProcessMemorySize
        {
            [System.Security.SecurityCritical]
            get {
                Int64 i64LastSurvivedProcessMemory = nGetLastSurvivedProcessMemorySize();
                if (i64LastSurvivedProcessMemory == -1)
                {
                    throw new InvalidOperationException(Environment.GetResourceString("InvalidOperation_WithoutARM"));
                }
                return i64LastSurvivedProcessMemory;
            }
        }
#endif
    }

    /// <summary>
    ///     Handle used to marshal an AppDomain to the VM (eg QCall). When marshaled via a QCall, the target
    ///     method in the VM will recieve a QCall::AppDomainHandle parameter.
    /// </summary>
    internal struct AppDomainHandle
    {
        private IntPtr m_appDomainHandle;

        // Note: generall an AppDomainHandle should not be directly constructed, instead the
        // code:System.AppDomain.GetNativeHandle method should be called to get the handle for a specific
        // AppDomain.
        internal AppDomainHandle(IntPtr domainHandle)
        {
            m_appDomainHandle = domainHandle;
        }
    }
}
