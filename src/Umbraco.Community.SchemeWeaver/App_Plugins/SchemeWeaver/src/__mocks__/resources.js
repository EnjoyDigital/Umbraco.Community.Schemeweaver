export async function tryExecuteAndNotify(host, promise) {
  try {
    const result = await promise;
    return { data: result.data, error: undefined };
  } catch (error) {
    return { data: undefined, error: { message: error.message || 'Unknown error' } };
  }
}
