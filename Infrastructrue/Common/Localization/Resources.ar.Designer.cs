﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Infrastructrue.Common.Localization {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources_ar {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources_ar() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Infrastructrue.Common.Localization.Resources.ar", typeof(Resources_ar).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to تم تعطيل حسابك، الرجاء إبلاغ مشرفي النظام.
        /// </summary>
        internal static string DeactivatedUser {
            get {
                return ResourceManager.GetString("DeactivatedUser", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to نوع الوحدة يجب أن تكون Governorate or Neighborhood or Block or Street or Parcel.
        /// </summary>
        internal static string FeatureMessage {
            get {
                return ResourceManager.GetString("FeatureMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to فشل في تنفيذ العملية مع ال Feature Service.
        /// </summary>
        internal static string FeatureServerError {
            get {
                return ResourceManager.GetString("FeatureServerError", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to اسم المستخدم أو كلمة السر غير صحيحين.
        /// </summary>
        internal static string InvalidUserNameOrPassword {
            get {
                return ResourceManager.GetString("InvalidUserNameOrPassword", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to {PropertyName} يجب أن تحوي قيمة.
        /// </summary>
        internal static string MissingValidValueMessage {
            get {
                return ResourceManager.GetString("MissingValidValueMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to المنطقة.
        /// </summary>
        internal static string Neighborhood {
            get {
                return ResourceManager.GetString("Neighborhood", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to فشل في استرجاع المعلومات من الهيئة العامة للمعلومات المدنية.
        /// </summary>
        internal static string PaciDataRetrievalFailure {
            get {
                return ResourceManager.GetString("PaciDataRetrievalFailure", resourceCulture);
            }
        }
    }
}
