﻿export interface JsObjectReference {
    __jsObjectId: number;
}

export interface DotnetObject {
    /**
     * Invokes the specified .NET instance public method synchronously. Not all hosting scenarios support
     * synchronous invocation, so if possible use invokeMethodAsync instead.
     *
     * @param methodIdentifier The identifier of the method to invoke. The method must have a [JSInvokable] attribute specifying this identifier.
     * @param args Arguments to pass to the method, each of which must be JSON-serializable.
     * @returns The result of the operation.
     */
    invokeMethod<T>(methodIdentifier: string, ...args: any[]): T;

    /**
     * Invokes the specified .NET instance public method asynchronously.
     *
     * @param methodIdentifier The identifier of the method to invoke. The method must have a [JSInvokable] attribute specifying this identifier.
     * @param args Arguments to pass to the method, each of which must be JSON-serializable.
     * @returns A promise representing the result of the operation.
     */
    invokeMethodAsync<T>(methodIdentifier: string, ...args: any[]): Promise<T>;

    /**
     * Dispose the specified .NET instance.
     */
    dispose(): void;
}